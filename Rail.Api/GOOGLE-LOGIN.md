# Google login through the Rail API

Google authentication, account creation/linking, and Rail token issuance run in the API.
The UI opens the system browser and calls the API; it never receives the Google client
secret or Google access tokens. No database migration is needed: Identity's existing
`AspNetUserLogins` table stores the Google subject ID.

## Configure locally

Keep the Web application OAuth client and this exact authorized redirect URI in Google Cloud:

```text
https://localhost:7130/signin-google
```

Rotate the client secret previously shared in chat. Store the replacement locally:

```powershell
dotnet user-secrets set "Authentication:Google:ClientSecret" "YOUR_NEW_SECRET" --project Rail.Api
dotnet run --project Rail.Api --launch-profile https
```

The public client ID is already in `appsettings.json`. The API project has a
`UserSecretsId`, so development startup loads the secret automatically. Restart the
API after configuring credentials. Without both values, Google start returns 503;
password authentication still works.

For production, supply `Authentication__Google__ClientId` and
`Authentication__Google__ClientSecret` through deployment secrets. Register
`https://YOUR_API_HOST/signin-google` in Google Cloud and configure the API's allowed
hosts and trusted reverse proxy so generated URLs use its public HTTPS origin.
Only the standard OpenID/profile/email scopes are requested. While the Google OAuth
app is in testing, add the intended Google accounts as test users in Google Cloud.

Google's setup reference: https://learn.microsoft.com/aspnet/core/security/authentication/social/google-logins

## UI contract

1. Call `POST /api/authentication/google/start` with no body. The API returns:

   ```json
   {
     "sessionId": "opaque-session-id",
     "exchangeKey": "private-exchange-key",
     "authorizationUrl": "https://localhost:7130/api/Authentication/google?sessionId=...",
     "expiresAt": "2026-11-11T12:05:00+00:00"
   }
   ```

2. Keep the session ID and exchange key in memory. Open `authorizationUrl` in the
   **system browser**, not an embedded WebView. The API redirects to Google, exchanges
   the Google authorization code on the server, validates OAuth state/correlation,
   and displays a message asking the user to return to Rail. `/signin-google` is
   handled by authentication middleware, not by a controller action.

3. Poll `POST /api/authentication/google/exchange` every two seconds, stopping at
   `expiresAt`, with this JSON body:

   ```json
   { "sessionId": "opaque-session-id", "exchangeKey": "private-exchange-key" }
   ```

   The Rail account is created during this exchange, not during the browser callback.
   When testing in Swagger or an HTTP client, call this endpoint yourself after
   completing Google login, using both values returned by `google/start`.

   - `202`, `{ "status": "pending" }`: Google login hasn't finished. Retry after two seconds.
   - `200`: the standard Identity bearer response (`tokenType`, `accessToken`,
     `expiresIn`, `refreshToken`), exactly as for password login. Store tokens securely
     and use the existing `refresh`, `me`, and `logout` endpoints.
   - `401`, `code: "requires_two_factor"`: ask for the Rail authenticator or recovery
     code, then retry the same exchange with `twoFactorCode` or
     `twoFactorRecoveryCode`. `invalid_two_factor` allows retry until lockout.
   - Other `401`: invalid/expired session, cancelled/failed Google login, or an account
     that cannot sign in. Stop polling and start a new flow if appropriate.
   - `409`, `code: "account_link_required"`: a Rail account already uses that email.
     Sign in with its existing credentials and use the linking flow below.
   - `400`: validation or account creation failed; show the returned error.

Successful exchange is single use. The browser only sees the session ID, while token
exchange requires the separate key returned to the initiating UI. Never put the key
or Rail tokens in a URL or log them. New accounts require Google's verified email;
existing accounts are looked up by Google's stable subject, not by email.

Google-only accounts initially have no password. They can use the existing
forgot/reset-password flow to set one before using password-required operations
such as `change-password` or account deletion. This requires configured SMTP.

## Test protected endpoints in Swagger

After login or exchange returns tokens, click **Authorize** in Swagger and paste
only the `accessToken` value. Swagger supplies the `Bearer` prefix and sends the
`Authorization` header on protected operations such as `GET /api/Authentication/me`.
Google exchange also accepts the header for the authenticated linking flow while
remaining available anonymously for normal Google login. Replace the token in
Authorize after it expires or after refreshing it.

## Link an existing Rail account

While signed in, call `POST /api/authentication/google/link/start` with the Rail
bearer token, then open the returned URL as above. Call `google/exchange` with the
same Rail bearer token and the returned session ID/key. A successful link returns
`200`, `{ "status": "linked" }`. It does not replace the current Rail tokens.

The exchange must authenticate as the Rail account that started linking. A Google
account already linked to another Rail account cannot be linked again. Matching
email addresses alone never authorize linking. Later Google logins still enforce
Rail email-confirmation, lockout, and two-factor requirements.

## Hosting and client status

Login sessions last five minutes and are stored in bounded API memory. Restarting
the API discards pending logins. This implementation targets one API instance;
multiple replicas require session affinity or a shared store with atomic single-use
exchange, and shared ASP.NET Core data protection keys.

MAUI currently hosts the offline prototype. This change supplies the API endpoints;
the client still needs to call them, launch the system browser, and store Rail tokens.
On a phone, `localhost` points to the phone: configure a browser-accessible HTTPS API
host and register that host's callback in Google Cloud when testing on devices.

## Verification

```powershell
dotnet test Rail.Api.Tests/Rail.Api.Tests.csproj
```

The integration tests use the real Google middleware with a fake Google HTTP
backchannel and an isolated in-memory Identity database. They do not contact Google
or the configured SQL Server. Real Google consent/login still requires a manual
browser check with the configured client credentials.
