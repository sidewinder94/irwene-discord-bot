using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Azure.KeyVault;
using Microsoft.Azure.Services.AppAuthentication;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.AzureKeyVault;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.AzureAppServices;

namespace irwene_discord_bot_aspcore
{
    public class Program
    {
        public static void Main(string[] args)
        {
            CreateHostBuilder(args).Build().Run();
        }

        public static IHostBuilder CreateHostBuilder(string[] args) =>
       Host.CreateDefaultBuilder(args)
           .ConfigureLogging(logging =>
           {
               logging.AddAzureWebAppDiagnostics();
           }).ConfigureServices(serviceCollection =>
           {
               serviceCollection
                   .Configure<AzureFileLoggerOptions>(options =>
                   {
                       options.FileName = "azure-diagnostics-";
                       options.FileSizeLimit = 50 * 1024;
                       options.RetainedFileCountLimit = 5;
                   });
           })
           
           .ConfigureAppConfiguration((context, config) =>
           {
               var keyVaultEndpoint = GetKeyVaultEndpoint();
               if (!string.IsNullOrEmpty(keyVaultEndpoint))
               {
                   var azureServiceTokenProvider = new AzureServiceTokenProvider();
                   var keyVaultClient = new KeyVaultClient(
                       new KeyVaultClient.AuthenticationCallback(
                           azureServiceTokenProvider.KeyVaultTokenCallback));
                   config.AddAzureKeyVault(keyVaultEndpoint, keyVaultClient, new DefaultKeyVaultSecretManager());
               }
           })
           .ConfigureWebHostDefaults(webBuilder =>
           {
               webBuilder.UseStartup<Startup>();
           });
        private static string GetKeyVaultEndpoint() => "https://kv-discord-bot.vault.azure.net/";
    }
}
