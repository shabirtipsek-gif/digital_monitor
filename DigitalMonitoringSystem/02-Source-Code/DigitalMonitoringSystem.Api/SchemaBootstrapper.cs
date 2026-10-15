using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DigitalMonitoringSystem.Api;

/// <summary>
/// Database.Migrate is unavailable here because the project carries no migrations and the
/// dotnet-ef tool is not a dependency. EnsureCreated only builds a schema when the database is
/// empty, so an mdm.db created before the metrics/alert-rule tables existed would fail every
/// query with "no such table". This step creates just the newer tables and indexes, and is
/// idempotent so it is safe to run on every start.
/// </summary>
public static class SchemaBootstrapper
{
    private static readonly string[] TableStatements =
    {
        """
        CREATE TABLE IF NOT EXISTS "DeviceMetrics" (
            "Id" TEXT NOT NULL CONSTRAINT "PK_DeviceMetrics" PRIMARY KEY,
            "DeviceId" TEXT NOT NULL,
            "Cpu" INTEGER NOT NULL,
            "Memory" INTEGER NOT NULL,
            "Disk" INTEGER NOT NULL,
            "CapturedAt" TEXT NOT NULL,
            "CapturedAtEpochMs" INTEGER NOT NULL DEFAULT 0
        )
        """,
        """
        CREATE TABLE IF NOT EXISTS "AlertRules" (
            "Id" TEXT NOT NULL CONSTRAINT "PK_AlertRules" PRIMARY KEY,
            "Name" TEXT NOT NULL,
            "Metric" TEXT NOT NULL,
            "Comparison" TEXT NOT NULL,
            "Threshold" INTEGER NOT NULL,
            "DurationMinutes" INTEGER NOT NULL,
            "Severity" TEXT NOT NULL,
            "Enabled" INTEGER NOT NULL,
            "UpdatedAt" TEXT NOT NULL
        )
        """,
        """
        CREATE TABLE IF NOT EXISTS "PolicyAssignments" (
            "Id" TEXT NOT NULL CONSTRAINT "PK_PolicyAssignments" PRIMARY KEY,
            "DeviceId" TEXT NOT NULL,
            "PolicyId" TEXT NOT NULL,
            "State" TEXT NOT NULL,
            "EnforcedBy" TEXT NOT NULL,
            "EnforcedAt" TEXT NOT NULL,
            "Message" TEXT NOT NULL
        )
        """,
        """
        CREATE TABLE IF NOT EXISTS "MobilePairings" (
            "Id" TEXT NOT NULL CONSTRAINT "PK_MobilePairings" PRIMARY KEY,
            "PairingCode" TEXT NOT NULL,
            "DeviceName" TEXT NOT NULL,
            "Platform" TEXT NOT NULL,
            "DeviceId" TEXT NOT NULL,
            "EnrollmentToken" TEXT NOT NULL,
            "ExpiresAt" TEXT NOT NULL,
            "Used" INTEGER NOT NULL
        )
        """
    };

    /// <summary>Columns added after the tables above were first introduced.</summary>
    private static readonly (string Table, string Column, string Definition)[] BackfilledColumns =
    {
        ("Alerts", "RuleId", "TEXT NOT NULL DEFAULT ''"),
        ("DeviceMetrics", "CapturedAtEpochMs", "INTEGER NOT NULL DEFAULT 0")
    };

    /// <summary>
    /// Indexes run last: SQLite validates the column list of an index, so a column that still has
    /// to be back-filled would make index creation fail.
    /// </summary>
    private static readonly string[] IndexStatements =
    {
        """
        CREATE INDEX IF NOT EXISTS "IX_DeviceMetrics_DeviceId_CapturedAtEpochMs"
            ON "DeviceMetrics" ("DeviceId", "CapturedAtEpochMs")
        """,
        """
        CREATE UNIQUE INDEX IF NOT EXISTS "IX_PolicyAssignments_PolicyId_DeviceId"
            ON "PolicyAssignments" ("PolicyId", "DeviceId")
        """,
        """
        CREATE UNIQUE INDEX IF NOT EXISTS "IX_MobilePairings_PairingCode"
            ON "MobilePairings" ("PairingCode")
        """
    };

    public static void Apply(DbContext context, ILogger? logger = null)
    {
        if (!context.Database.IsRelational())
        {
            return;
        }

        var providerName = context.Database.ProviderName;
        if (providerName is null || !providerName.Contains("Sqlite", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        foreach (var statement in TableStatements)
        {
            context.Database.ExecuteSqlRaw(statement);
        }

        // A table created by an earlier build already exists, so CREATE TABLE IF NOT EXISTS is a
        // no-op there and any column added since then still has to be back-filled.
        foreach (var (table, column, definition) in BackfilledColumns)
        {
            EnsureColumn(context, table, column, definition);
        }

        foreach (var statement in IndexStatements)
        {
            context.Database.ExecuteSqlRaw(statement);
        }

        logger?.LogInformation(
            "Verified {Tables} tables, {Columns} columns and {Indexes} indexes for the SQLite store.",
            TableStatements.Length,
            BackfilledColumns.Length,
            IndexStatements.Length);
    }

    /// <summary>
    /// Adds a column when the table exists but predates it. SQLite has no
    /// "ADD COLUMN IF NOT EXISTS", so the current shape is read from PRAGMA first.
    /// </summary>
    private static void EnsureColumn(DbContext context, string table, string column, string definition)
    {
        var connection = context.Database.GetDbConnection();
        var opened = false;
        if (connection.State != ConnectionState.Open)
        {
            connection.Open();
            opened = true;
        }

        try
        {
            using (var command = connection.CreateCommand())
            {
                command.CommandText = $"PRAGMA table_info(\"{table}\")";
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase))
                    {
                        return;
                    }
                }
            }

            context.Database.ExecuteSqlRaw($"ALTER TABLE \"{table}\" ADD COLUMN \"{column}\" {definition}");
        }
        finally
        {
            if (opened)
            {
                connection.Close();
            }
        }
    }

    /// <summary>
    /// Ensures the schema exists and then tops up the tables added after the initial release.
    /// </summary>
    public static void Initialize(MdmDbContext context, ILogger? logger = null)
    {
        context.Database.EnsureCreated();
        Apply(context, logger);
    }

    /// <summary>
    /// Returns the tables the current model expects, used by diagnostics to spot a stale database.
    /// </summary>
    public static IReadOnlyList<string> ExpectedTables(DbContext context)
    {
        return context.Model.GetEntityTypes()
            .Select(entityType => entityType.GetTableName())
            .OfType<string>()
            .Distinct()
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();
    }
}
