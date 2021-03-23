using System;
using SendGrid.Helpers.Mail;
using System.Threading;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Extensions.Logging;
using Azure.Identity;
using Azure.ResourceManager.Resources;
using Azure.ResourceManager.Resources.Models;

namespace SpringClean
{
    public static class ExtendExpirationOrchestration
    {
        [FunctionName(nameof(ExtendExpirationOrchestrationTrigger))]
        public static async Task ExtendExpirationOrchestrationTrigger(
            [OrchestrationTrigger] IDurableOrchestrationContext context)
        {
            var param = context.GetInput<ExtendModel>();
            param.InstanceId = context.InstanceId;

            await context.CallActivityAsync("SendExtensionRequest", param);

            using (var timeoutCts = new CancellationTokenSource())
            {
                param.ResponseExpires = context.CurrentUtcDateTime.AddHours(param.ExtendHours);

                DateTime dueTime = param.ResponseExpires;
                Task durableTimeout = context.CreateTimer(dueTime, timeoutCts.Token);

                Task<bool> extendEvent = context.WaitForExternalEvent<bool>("ExtendExpiration");
                
                if (extendEvent == await Task.WhenAny(extendEvent, durableTimeout))
                {
                    timeoutCts.Cancel();

                    //extend the time of the expiration by configured amount
                    await context.CallActivityAsync("ExtendExpiration",param);
                }
                else
                {
                    //delete the resource
                    await context.CallActivityAsync("DeleteResource", param);
                }
            }
        }

        [FunctionName(nameof(SendExtensionRequest))]
        public static async Task SendExtensionRequest([ActivityTrigger] ExtendModel settings,
                                                [SendGrid(ApiKey = "SendGridApiKey")] IAsyncCollector<SendGridMessage> messageCollector,
                                                ILogger log)
        {
            log.LogInformation($"Sending Extension Request email for {settings.ResourceGroupName} to {settings.ExpirationEmail}.");

            var linkUrl = Environment.GetEnvironmentVariable("WEBSITE_HOSTNAME");
            var fromEmail = Environment.GetEnvironmentVariable("FromEmail");
            var extendUrl = $"{linkUrl}/api/extend/{settings.InstanceId}";
            var subject = $"Your Resource Group {settings.ResourceGroupName} is about to be deleted.";
            var messageBody = $@"FYI: Your Resource Group {settings.ResourceGroupName} is scheduled to be deleted. 
                                If you don't respond by {settings.ResponseExpires} (UTC) your resource group will be deleted.  
                                Visit {extendUrl} to extend your resource group for another {settings.ExtendHours} hours.
                                No action is neccesary if you would like these resources deleted. ";

            var message = new SendGridMessage();
            message = new SendGridMessage();
            message.AddTo(settings.ExpirationEmail);
            message.AddContent("text/html", messageBody);
            message.SetFrom(new EmailAddress(fromEmail));
            message.SetSubject(subject);

            await messageCollector.AddAsync(message);
            
        }

        [FunctionName(nameof(ExtendExpiration))]
        public static async Task ExtendExpiration([ActivityTrigger] ExtendModel settings, 
                                                ILogger log)
        {

            log.LogInformation($"Extending Expiration for {settings.ResourceGroupName} to {settings.ExpirationEmail}.");

            var subscriptionId = Environment.GetEnvironmentVariable("SubscriptionId");
            
            var credentials = new DefaultAzureCredential();
            var resourceClient = new ResourcesManagementClient(subscriptionId, credentials);
            
            var resourceGroupResult = await resourceClient.ResourceGroups.GetAsync(settings.ResourceGroupName);
            var resourceGroup = resourceGroupResult.Value;

            var expireDateTag = resourceGroup.Tags["expiration-date"];
            var expireDate = DateTime.Parse(expireDateTag);

            //Update the Resource Group with the new expiration date
            var newExpireDate = expireDate.AddHours(settings.ExtendHours);
            resourceGroup.Tags["expiration-date"] = newExpireDate.ToLongTimeString();

            await resourceClient.ResourceGroups.CreateOrUpdateAsync(settings.ResourceGroupName,resourceGroup);

        }

        [FunctionName(nameof(DeleteResource))]
        public static async Task DeleteResource([ActivityTrigger] ExtendModel settings, 
                                                ILogger log)
        {
            log.LogInformation($"Deleting {settings.ResourceGroupName} to {settings.ExpirationEmail}.");

            var subscriptionId = Environment.GetEnvironmentVariable("SubscriptionId");
            
            var credentials = new DefaultAzureCredential();
            var resourceClient = new ResourcesManagementClient(subscriptionId, credentials);
            
            await resourceClient.ResourceGroups.StartDeleteAsync(settings.ResourceGroupName);
            
        }

        [FunctionName(nameof(ExtendExpirationHttpTrigger))]
        public static async Task<IActionResult> ExtendExpirationHttpTrigger(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route="extend/{instanceid}")] HttpRequestMessage req,
            [DurableClient] IDurableOrchestrationClient client,
            string instanceid,
            ILogger log)
        {

            int extensionHours = int.Parse(Environment.GetEnvironmentVariable("ExtendHours"));

            await client.RaiseEventAsync(instanceid, "ApprovalEvent");

            var message = $@"<html><head>Resource Group Expiration Extended</head>
                             <body><h2>Your resource group expiration has been increased by {extensionHours} hours. </h2> <p>You will receive another
                             notification again prior to deletion.</p></body><html>";
            
            return new ContentResult { Content = message, ContentType = "text/html" };
   
        }
    }
}