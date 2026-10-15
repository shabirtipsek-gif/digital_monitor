# Live Monitoring Rollout Requirements

## Purpose

This document defines the information, approvals, infrastructure, and rollout checks required to put 200 or more district computers on live monitoring using the Digital Monitoring System. Capacity beyond the pilot must be confirmed under the district's actual workload.

Live monitoring means that an installed agent sends CPU, memory, and disk usage to the monitoring server approximately every 30 seconds. It does not, by itself, verify asset ownership, provide remote control, or enforce Windows policies.

## Start with the prepared tracker

Use [`200-DEVICE-LIVE-MONITORING-TRACKER.csv`](./200-DEVICE-LIVE-MONITORING-TRACKER.csv) to coordinate verification and rollout. It has 200 candidate rows and columns for actual asset identity, evidence, approvals, installation, enrollment, and heartbeat checks.

- The dashboard also starts with 200 clearly labeled demo-only inventory rows. These are not assets, are not enrolled, and do not send monitoring data. They use documentation-only IP addresses and have no reported metrics.
- The system can enroll additional real devices beyond those demo rows. A device is live only after its own approved agent is installed and sends telemetry.
- `SAMPLE-ROW-001` through `SAMPLE-ROW-200` are tracker row references only.
- `SourceAssetReference` repeats the source list's example ID for reconciliation only; the source explicitly describes those IDs as fictional examples.
- Actual district asset ID, hostname, IP address, serial number, evidence, approval, device ID, and monitoring results are intentionally blank.
- Do not change a row to verified, approved, or reporting until the responsible person has actually completed and recorded that check.
- Never store enrollment tokens or administrator passwords in this tracker. Treat them as credentials and store them only in an approved secure location.

For every actual computer, replace the candidate reference information with confirmed information from the physical device and district records. If the source list does not correspond to a real computer, mark that row not applicable and create a row for the actual device as appropriate.

## 1. Required approval and asset verification

Before enrolling a computer:

- Obtain approval from the responsible district authority to monitor the computer and collect its operational metrics.
- Record the approved scope, purpose, metrics collected (CPU, memory, disk), responsible administrator, intended start date, retention/back-up arrangement, and review/incident contact using the district's authorized approval process.
- Follow district policy for notifying affected staff and obtaining any required privacy, records-management, or legal review.
- Confirm that the physical computer exists and that the district is authorized to manage it.
- Check the asset against an authorized district asset register and, where necessary, purchase/transfer records and a physical inspection.
- Do not treat the 200 entries in the current pending-verification list as verified assets. Their supplied source marks them as fictional test records and uses placeholder serial numbers and models marked “To verify.”

## 2. Information to collect for each computer

Prepare one verified row per real computer. Collect:

| Field | Requirement |
|---|---|
| Asset ID | Confirmed district asset-register ID, if assigned |
| Hostname | Current Windows computer name; required for enrollment |
| IP address | Current address reachable on the district network; required for enrollment |
| Department and office | Verified location for support and reporting |
| Manufacturer and model | Read from the computer label or system information |
| Serial number | Read from the physical device and reconcile with the asset register |
| Windows version | Record the installed edition/version and confirm the device is supported |
| Responsible contact | Authorized department contact for installation and support |
| Installation status | Not started, installed, reporting, or issue |
| Enrollment status | Not enrolled, enrolled, or agent configuration pending |

Do not invent hostnames, IP addresses, serial numbers, staff names, ownership, or verification status. A computer without a verified hostname and IP address cannot be enrolled using the current dashboard workflow.

### How to collect the missing computer details

For each physically available, authorized Windows computer, have district IT or an authorized technician:

1. Match the device's asset label and serial number to the district asset register; record which approved record or document was checked.
2. Record the computer name (`hostname` in Command Prompt) and Windows edition/version (`winver`).
3. Record the current network address (`ipconfig`); use the address that can reach the monitoring server. Ask network staff whether it is static, DHCP-reserved, or dynamic, and record the appropriate current/reserved address.
4. Read manufacturer, model, and serial number from the physical label and, where available, Windows system information. Resolve any mismatch against the official asset register before approval.
5. Have the authorized approver record the approval reference, date, and scope in the tracker.

These steps collect factual device details; they do not independently prove district ownership. The asset register and supporting district records remain the authority for ownership.

## 3. Server requirements

- Designate one managed Windows computer or server to host the ready-to-run system.
- Keep it powered on and connected to the district network whenever monitoring is required.
- Start the system from `01-Ready-to-Run\START-SYSTEM.bat` or the top-level `START-SYSTEM.bat`.
- Confirm the health check at `http://<server-address>:5103/health` returns `status: ok`.
- Change the packaged default administrator password before operational use and restrict administrator access to authorized staff.
- Identify who is responsible for server updates, incident response, database backups, and restoring service.
- Back up `01-Ready-to-Run\data\mdm.db` on a defined schedule. Protect backups and test restoration.
- Run a pilot with a small group of computers before approving full rollout. Server capacity for 200 devices has not been formally benchmarked; observe resource usage and stability during the pilot.
- At a 30-second agent interval, 200 continuously reporting computers produce about 6.7 heartbeats per second on average. Check API responsiveness, storage growth, CPU, memory, and recovery under pilot load; this estimate does not replace a capacity test.

## 4. Network and endpoint requirements

- The server and monitored computers must be able to communicate over the district network.
- Permit endpoint-to-server TCP traffic on port `5103`. Apply the firewall rule only to trusted district network profiles and restrict source networks where practical.
- Give the server a stable address (static IP or reserved DHCP address) and use that address when configuring agents.
- Confirm endpoint firewall/proxy rules do not block the connection.
- Confirm endpoint clocks are reasonably synchronized so event times are useful.
- Install the supplied monitoring agent on each authorized computer. The ready-to-run package includes a Node.js runtime in the agent folder; use the supplied runtime and scripts.
- Configure each agent with that computer’s API address, Device ID, and unique enrollment token. Do not reuse another computer’s token.
- Start the agent and configure authorized automatic startup using the supplied `INSTALL-AUTOSTART.bat` when approved by district IT.
- The packaged system uses HTTP. Keep it on a trusted, access-controlled network; do not expose port 5103 directly to the public internet. If monitoring must cross an untrusted network, first deploy an appropriately secured HTTPS arrangement.

## 5. Enrollment procedure

Repeat for each verified and approved computer:

1. Complete and save that device's row in `200-DEVICE-LIVE-MONITORING-TRACKER.csv`, including asset verification and monitoring approval.
2. In the administrator dashboard, enroll the computer using its actual hostname and IP address. Add the verified vendor, platform, and location where available.
3. Record the returned Device ID in the tracker. Save the enrollment token securely outside the tracker; the token is a credential.
4. Copy the agent folder to the computer using an approved installation method.
5. Put the API address, Device ID, and enrollment token in that agent's `appsettings.json`.
6. Start the agent with `RUN-AGENT.bat`.
7. Confirm the dashboard reports the computer online and shows current CPU, memory, and disk metrics.
8. If approved, install automatic startup with `INSTALL-AUTOSTART.bat` run as administrator.
9. Record the successful installation date, operator, first heartbeat, dashboard status, and any exceptions in the rollout tracker.

## 6. Pilot and acceptance criteria

First enroll a small, representative pilot group from more than one department or location. Do not begin mass rollout until the pilot passes these checks:

- Each pilot computer matches a verified asset and authorized monitoring record.
- Each agent connects to the intended server with its own token.
- Dashboard hostname, location, and online status match the roster.
- CPU, memory, and disk telemetry updates on the expected interval (approximately 30 seconds).
- Stopping an agent eventually causes the expected offline status (the configured default is about 15 minutes); restarting the agent restores reporting.
- The server remains responsive while the pilot agents report, and the database continues to be written.
- Startup behavior, firewall scope, administrator access, and backup/restore responsibilities are confirmed.

For final rollout acceptance, reconcile the roster against the dashboard and report:

- Number of computers authorized and verified.
- Number successfully enrolled and reporting.
- Number not reporting, with a named owner and resolution plan for each exception.
- Confirmation that no fictional sample entry was counted as an operational device.

Do not claim the 200-device rollout is complete solely because 200 inventory rows exist. Completion requires actual, authorized computers to be installed, enrolled, and reporting.

## 7. Operational responsibilities

Assign named district roles (and record the actual people through the district's approved process) for:

- Monitoring-system administrator and credential custody.
- Endpoint deployment and agent updates.
- Network/firewall and server maintenance.
- Database backup and recovery.
- Review of alerts and response to offline computers.
- Asset-register reconciliation and approval of changes.

The current policy feature records policy assignments; it does not automatically enforce Windows settings. Keep the rollout scope limited to the telemetry and alert functions that have actually been tested and approved.
