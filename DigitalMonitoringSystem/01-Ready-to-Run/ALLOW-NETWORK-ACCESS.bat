@echo off
rem Run this ONCE as Administrator (right-click - Run as administrator) so other computers can reach the server.
netsh advfirewall firewall add rule name="Digital Monitoring System" dir=in action=allow protocol=TCP localport=5103
echo.
echo Done. Other computers can now open http://THIS-COMPUTER-IP:5103
pause
