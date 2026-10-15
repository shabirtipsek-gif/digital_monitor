DIGITAL MONITORING SYSTEM
========================

TO START:
  Double-click START-SYSTEM.bat in this folder.
  Sign in to the dashboard in your browser.

FOLDERS:
  01-Ready-to-Run       Complete system and bundled runtime. Start here.
  02-Source-Code        C# API, agent, desktop app, tests, and web dashboard source.
  03-Agent-for-Computers
                        Copy this folder to another computer to install its monitoring agent.

ROLLOUT TRACKER:
  Use 200-DEVICE-LIVE-MONITORING-TRACKER.csv to record asset verification, approvals,
  actual hostnames/IP addresses, agent setup, enrollment, and reporting status.
  Its 200 source references are examples explicitly marked fictional; actual device details
  and evidence are blank and must be completed by authorized district staff. Do not place
  device tokens or passwords in this CSV.
  See LIVE-MONITORING-ROLLOUT-REQUIREMENTS.md for the full preparation and rollout checklist.

The dashboard starts with 200 clearly labeled demo-only inventory records. They use documentation-only
IP addresses, have no agent tokens or heartbeat data, and are not real or monitored district devices.
The separate Pending verification view contains 200 fictional candidate records for review only. Neither
set is evidence that assets exist or belong to the district. Check an authorized asset register, purchase
documentation, and physically inspect each real device before recording ownership or enrolling it.
The system supports enrolling additional real devices; only installed agents send live monitoring data.

See 01-Ready-to-Run\README.txt for server setup, network access, and computer enrollment
instructions. Keep the server computer running while other computers are monitored.
