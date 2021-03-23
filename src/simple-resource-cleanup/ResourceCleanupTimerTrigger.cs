using System;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Extensions.Logging;
using Azure.Core;
using Azure.Identity;
using Azure.ResourceManager.Resources;
using Azure.ResourceManager.Resources.Models;

namespace SpringClean
{
    public class ResourceCleanupFunctions
    {
        [FunctionName(nameof(ResourceCleanupTimerTrigger))]
        public async Task ResourceCleanupTimerTrigger([TimerTrigger("0 */5 * * * *")]TimerInfo myTimer, 
            ILogger log)
        {

            log.LogInformation($"Executing Resource Cleanup at : {DateTime.Now}");

            var subscriptionId = Environment.GetEnvironmentVariable("SubscriptionId");
            var expireTagKey = Environment.GetEnvironmentVariable("ExpireTagKey");

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
                                
                                log.LogInformation($"{group.Name} resource group expired. Expires {expireDate} and todays date is {DateTime.Now}. Deleting Resource.");

                                await resourceGroups.StartDeleteAsync(group.Name);

                                log.LogInformation($"{group.Name} resource group successfully deleted.");
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
