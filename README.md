# Introduction 
TODO: Give a short introduction of your project. Let this section explain the objectives or the motivation behind this project. 

# Getting Started
TODO: Guide users through getting your code up and running on their own system. In this section you can talk about:
1.	Installation process
2.	Software dependencies
3.	Latest releases
4.	API references

# Build and Test
TODO: Describe and show how to build your code and run the tests. 

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