# API configuration

For local development, `appsettings.Development.json` contains a development JWT key and administrator credentials.

For deployment, set these environment variables instead:

```text
Jwt__Key=<at least 32 bytes>
Admin__Username=<administrator>
Admin__Password=<strong password>
Cors__AllowedOrigins__0=http://<trusted-frontend-host>:5173
Network__AdvertisedHost=<API LAN IP or DNS name>
```

The API uses HTTP port 5103 by default for offline/LAN operation. HTTPS redirection is disabled by default for this local deployment model and can be enabled with:

```text
Security__UseHttpsRedirection=true
```
