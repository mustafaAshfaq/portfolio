# Introduction 
TODO: Give a short introduction of your project. Let this section explain the objectives or the motivation behind this project. 

# Getting Started
TODO: Guide users through getting your code up and running on their own system. In this section you can talk about:
1.	Installation process
2.	Software dependencies
3.	Latest releases
4.	API references

# Build and Test

Install the .NET 10 SDK, then run:

```powershell
dotnet build portfolio.sln
dotnet test portfolio.sln
```

Tests use fake HTTP transports: they never send email or require SendGrid credentials.

# Contact inquiries

The existing Blazor form calls same-origin `POST /api/contact`. A managed Azure
Functions API sends the visitor's name and message through SendGrid to one
configured owner inbox, with the visitor's email as Reply-To. The sender and
recipient cannot be supplied by the visitor. The UI's SendGrid package is no
longer needed; the server uses SendGrid's HTTP API through `HttpClient`.

The API targets .NET 10 isolated, supported by the current
[Static Web Apps runtime table](https://learn.microsoft.com/azure/static-web-apps/languages-runtimes#api).
The UI remains on its existing .NET 9 framework. The deployment workflow tests
the solution and publishes the API with the .NET 10 SDK, then uploads it as the
managed API. The frontend configuration pins `dotnet-isolated:10.0`.

## Server configuration

In the Azure Static Web App's **Environment variables** settings, add the
following for each environment where contact email should work:

| Setting | Value |
| --- | --- |
| `SendGrid__ApiKey` | SendGrid API key with Mail Send permission |
| `SendGrid__SenderEmail` | Your verified SendGrid sender |
| `SendGrid__RecipientEmail` | Your fixed owner inbox |

These are backend settings, not frontend appsettings or workflow output.
Never commit keys or put them in browser assets. Production configuration is
not automatically a safe choice for PR preview environments; configure those
separately or leave email unavailable there. Enable Application Insights on the
Static Web App to inspect API failures. Application logs contain only generic
outcomes and provider status codes, not visitor addresses, messages, keys, or
provider response bodies.

## Run locally

Install Azure Functions Core Tools v4 with .NET 10 support (validated with 4.16.0), Azurite, and the Azure
Static Web Apps CLI. From the repository root:

```powershell
Copy-Item Api\local.settings.example.json Api\local.settings.json
```

Fill in the three SendGrid settings in the ignored local file. Use a test inbox.
Run Azurite, then start these in separate terminals:

```powershell
# Terminal 1
Set-Location Api
func start --port 7071

# Terminal 2, from the repository root
dotnet run --project Portfolio.UI --launch-profile http

# Terminal 3, from the repository root
swa start http://localhost:5161 --api-location http://localhost:7071
```

Browse the SWA proxy at `http://localhost:4280`, not the UI development port,
so `/api/contact` reaches Functions without CORS configuration.

## Contract and failure behavior

The JSON body has `name`, `email`, `message`, and optional `honeypot`. After
trimming, name must be 1-100 characters, email valid and at most 254 characters,
and message 1-5,000 characters. The entire body is limited to 64 KiB.
A nonempty honeypot is rejected without sending email.

- `202`: SendGrid accepted the message; this is **not** an inbox-delivery guarantee.
- `400`: invalid JSON, fields, or honeypot.
- `413`: body too large.
- `502`: provider rejection or network failure.
- `503`: missing or invalid server configuration.
- `504`: provider timeout; delivery is uncertain.

There are no automatic retries. The form disables submission while pending,
retains input on failure, and clears only after acceptance. A retry after a
timeout can produce a duplicate email. Honeypot-only protection is intentional:
bots can bypass it and consume email quota. CAPTCHA, additional rate limiting,
attachments, automatic replies, persistence, and queues are out of scope.

## Live smoke test (human verification)

1. Configure Azure/SendGrid and deploy. Open the deployed form.
2. Submit a recognizable test message. In browser Network tools, verify a
   same-origin POST to `/api/contact` returns `202`. Confirm the form clears and
   reports acceptance rather than guaranteed delivery.
3. Check the owner inbox (and spam folder) for the name and message. Verify
   **Reply** targets the visitor, and the From address is the verified sender.
4. Submit invalid fields and confirm validation. Using a REST client, also
   verify a nonempty `honeypot` returns `400` without sending email.
5. In a nonproduction environment, remove a required setting and verify `503`
   and preserved form input; restore the setting afterward.

Automated tests do not establish live inbox delivery or Azure deployment success.

# Google Tag Manager
The site loads a client-side Google Tag Manager web container when `GoogleTagManager:ContainerId` is set. A blank id loads nothing. The container id is public in the page, so it lives in appsettings rather than an environment variable.

- `Portfolio.UI/wwwroot/appsettings.Development.json` is used by `dotnet run` (local testing).
- `Portfolio.UI/wwwroot/appsettings.json` is used by the deployed site (production).

Both files currently use `GTM-5M5S7V89`. Tags are created and managed in the Tag Manager web UI, not in this repo.

## Test locally
1. Run the site: `dotnet run --project Portfolio.UI --launch-profile http` (serves `http://localhost:5161`).
2. In [Google Tag Manager](https://tagmanager.google.com/), open the container and click **Preview**.
3. Enter `http://localhost:5161` and connect. Tag Assistant lists tags under **Tags Fired** when they work.
4. To try a different container, change only `appsettings.Development.json`, restart the site, and preview again. Leave `appsettings.json` on the production id.

# Contribute
TODO: Explain how other users and developers can contribute to make your code better. 

If you want to learn more about creating good readme files then refer the following [guidelines](https://docs.microsoft.com/en-us/azure/devops/repos/git/create-a-readme?view=azure-devops). You can also seek inspiration from the below readme files:
- [ASP.NET Core](https://github.com/aspnet/Home)
- [Visual Studio Code](https://github.com/Microsoft/vscode)
- [Chakra Core](https://github.com/Microsoft/ChakraCore)