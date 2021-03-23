using System;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Extensions.Logging;
using Azure.Core;
using Azure.Identity;
using Azure.ResourceManager.Resources;
using Azure.ResourceManager.Resources.Models;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;

namespace SpringClean
{
    public class ResourceCleanupFunctions
    {
        [FunctionName(nameof(ResourceCleanupTimerTrigger))]
        public async Task ResourceCleanupTimerTrigger([TimerTrigger("0 */5 * * * *")]TimerInfo myTimer, 
            [DurableClient] IDurableOrchestrationClient client,
            ILogger log)
        {

            log.LogInformation($"Executing Resource Cleanup at : {DateTime.Now}");

            var subscriptionId = Environment.GetEnvironmentVariable("SubscriptionId");
            var expireTagKey = Environment.GetEnvironmentVariable("ExpireTagKey");
            int extensionHours = int.Parse(Environment.GetEnvironmentVariable("ExtendHours"));
            int responseExpirationHours = int.Parse(Environment.GetEnvironmentVariable("ResponseExpirationHours"));

            var credentials = new DefaultAzureCredential();
            var resourceClient = new ResourcesManagementClient(subscriptionId, credentials);
            
            var resourceGroups = resourceClient.ResourceGroups;
            var resourceGroupPages = resourceGroups.ListAsync($"tagName eq 'expiration-tag' and tagValue eq '{expireTagKey}'").AsPages();

            await foreach (Azure.Page<ResourceGroup> groupPage in resourceGroupPages)
            {
                foreach (var group in groupPage.Values)
                {
                    log.LogInformation($"Resource Group Name: {group.Name}");

                    var expireDateTagExists = group.Tags.ContainsKey("expiration-date");

                    if(expireDateTagExists){

                        var expireDateTag = group.Tags["expiration-date"];

                        var expireDate = default(DateTime);
                        var validDate = DateTime.TryParse(expireDateTag, out expireDate);

                        if(validDate){

                            if(DateTime.Now > expireDate) {

                                var expireEmailExists = group.Tags.ContainsKey("expiration-email");

                                // If we have a expiration email we start the Orchestration to allow the user
                                // to extend the time period
                                if(expireEmailExists){

                                       var email = group.Tags["expiration-date"];

                                       log.LogInformation($"{group.Name} resource group expired. Starting Extend Expiration Orchestration. Sending Email to ${email}.");

                                        var param = new ExtendModel() {
                                            ResourceGroupName = group.Name,
                                            ExpirationDate = expireDate,
                                            ExpirationEmail = email,
                                            ExtendHours= extensionHours,
                                            ResponseExpirationHours = responseExpirationHours
                                        };

                                        await client.StartNewAsync("ExtendExpirationOrchestrationTrigger",param);
                                }
                                else{

                                      log.LogInformation($"{group.Name} resource group expired. Expires {expireDate} and todays date is {DateTime.Now}. Deleting Resource.");

                                      await resourceGroups.StartDeleteAsync(group.Name);

                                      log.LogInformation($"{group.Name} resource group successfully deleted.");
                                }
                                
                             

                                await resourceGroups.StartDeleteAsync(group.Name);

                            }
                            else{
                                log.LogInformation($"{group.Name} not expired yet. Expires {expireDate}");
                            }
                            
                        }
                        else{
                             log.LogInformation($"{group.Name} resource group expiration-date value {expireDateTag} is not a valid date.");
                        }
                    }
                    else{
                        log.LogInformation($"{group.Name} resource group 'expiration-tag' missing.");
                    }
                }

            }

        }
    }
}
