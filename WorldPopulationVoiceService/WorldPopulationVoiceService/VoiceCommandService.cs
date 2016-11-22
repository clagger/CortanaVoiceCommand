using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.ApplicationModel.AppService;
using Windows.ApplicationModel.Background;
using Windows.ApplicationModel.Resources.Core;
using Windows.ApplicationModel.VoiceCommands;
using Windows.Storage;


namespace WorldPopulation.VoiceCommands
{
    public sealed class VoiceCommandService : IBackgroundTask
    {

        //connection to cortana for a session
        VoiceCommandServiceConnection voiceServiceConnection;

        // Lifetime of the background service is controlled via the BackgroundTaskDeferral object, including
        BackgroundTaskDeferral serviceDeferral;

        // ResourceMap containing localized strings for display in Cortana.
        ResourceMap cortanaResourceMap;

        // The context for localized string
        ResourceContext cortanaContext;

        //this function will be invoked when calling a VoiceCommand with the  <VoiceCommandService Target="...">
        //tag
        public async void Run(IBackgroundTaskInstance taskInstance)
        {
            serviceDeferral = taskInstance.GetDeferral();

            //register an event if cortana dismisses the background task
            taskInstance.Canceled += OnTaskCanceled;

            var triggerDetails = taskInstance.TriggerDetails as AppServiceTriggerDetails;

            // Load localized resources for strings sent to Cortana to be displayed to the user.
            cortanaResourceMap = ResourceManager.Current.MainResourceMap.GetSubtree("Resources");

            // Select the system language, which is what Cortana should be running as.
            cortanaContext = ResourceContext.GetForViewIndependentUse();

            // This should match the uap:AppService and VoiceCommandService references from the 
            // package manifest and VCD files, respectively. Make sure we've been launched by
            // a Cortana Voice Command.
            if (triggerDetails != null && triggerDetails.Name == "VoiceCommandService")
            {
                try
                {
                    voiceServiceConnection =
                        VoiceCommandServiceConnection.FromAppServiceTriggerDetails(
                            triggerDetails);

                    voiceServiceConnection.VoiceCommandCompleted += OnVoiceCommandCompleted;

                    // GetVoiceCommandAsync establishes initial connection to Cortana, and must be called prior to any 
                    // messages sent to Cortana. Attempting to use ReportSuccessAsync, ReportProgressAsync, etc
                    // prior to calling this will produce undefined behavior.
                    VoiceCommand voiceCommand = await voiceServiceConnection.GetVoiceCommandAsync();

                    // Depending on the operation (defined in AdventureWorks:AdventureWorksCommands.xml)
                    // perform the appropriate command.
                    switch (voiceCommand.CommandName)
                    {
                        case "showFuturePopulation":
                            var country = voiceCommand.Properties["country"][0];
                            var year = voiceCommand.Properties["year"][0];
                            await SendCompletionMessageForFuturePopulation(country, year);
                            break;
                        default:
                            break;
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine("Handling Voice Command failed " + ex.ToString());
                }
            }
        }

        //Search for the requested data and give a response in cortana
        private async Task SendCompletionMessageForFuturePopulation(string country, string year)
        {
            // If this operation is expected to take longer than 0.5 seconds, the task must
            // provide a progress response to Cortana prior to starting the operation, and
            // provide updates at most every 5 seconds.
            string calculatingPrediction = string.Format(
                       cortanaResourceMap.GetValue("CalculatingPrediction", cortanaContext).ValueAsString,
                       country, year);
            await ShowProgressScreen(calculatingPrediction);

            //call REST API from Azure Cloud which offers the needed data (Country and year are parameters)
            //just a placeholder value for test purpose
            var population = "20121212.2";

            var userMessage = new VoiceCommandUserMessage();
            var responseContentTile = new VoiceCommandContentTile();

            //set the type of the ContentTyle
            responseContentTile.ContentTileType = VoiceCommandContentTileType.TitleWithText;

            //fill the responseContentTile with the data we got
            responseContentTile.AppLaunchArgument = country;
            responseContentTile.Title = country + " " + year;

            responseContentTile.TextLine1 = "Population: " + population;

            //the VoiceCommandResponse needs to be a list
            var tileList = new List<VoiceCommandContentTile>();
            tileList.Add(responseContentTile);

            // Set a message for the Response Cortana Page
            string message = String.Format(cortanaResourceMap.GetValue("ShowFuturePopulation", cortanaContext).ValueAsString, country, year, population);

            userMessage.DisplayMessage = message;
            userMessage.SpokenMessage = message;

            var response = VoiceCommandResponse.CreateResponse(userMessage, tileList);

            await voiceServiceConnection.ReportSuccessAsync(response);
        }


        /// <summary>
        /// Show a progress screen. These should be posted at least every 5 seconds for a 
        /// long-running operation, such as accessing network resources over a mobile 
        /// carrier network.
        /// </summary>
        /// <param name="message">The message to display, relating to the task being performed.</param>
        /// <returns></returns>
        private async Task ShowProgressScreen(string message)
        {
            var userProgressMessage = new VoiceCommandUserMessage();
            userProgressMessage.DisplayMessage = userProgressMessage.SpokenMessage = message;

            VoiceCommandResponse response = VoiceCommandResponse.CreateResponse(userProgressMessage);
            await voiceServiceConnection.ReportProgressAsync(response);
        }

        /// <summary>
        /// Handle the completion of the voice command. Your app may be cancelled
        /// for a variety of reasons, such as user cancellation or not providing 
        /// progress to Cortana in a timely fashion. Clean up any pending long-running
        /// operations (eg, network requests).
        /// </summary>
        /// <param name="sender">The voice connection associated with the command.</param>
        /// <param name="args">Contains an Enumeration indicating why the command was terminated.</param>
        private void OnVoiceCommandCompleted(VoiceCommandServiceConnection sender, VoiceCommandCompletedEventArgs args)
        {
            if (this.serviceDeferral != null)
            {
                this.serviceDeferral.Complete();
            }
        }
        /// <summary>
        /// When the background task is cancelled, clean up/cancel any ongoing long-running operations.
        /// This cancellation notice may not be due to Cortana directly. The voice command connection will
        /// typically already be destroyed by this point and should not be expected to be active.
        /// </summary>
        /// <param name="sender">This background task instance</param>
        /// <param name="reason">Contains an enumeration with the reason for task cancellation</param>
        private void OnTaskCanceled(IBackgroundTaskInstance sender, BackgroundTaskCancellationReason reason)
        {
            System.Diagnostics.Debug.WriteLine("Task cancelled, clean up");
            if (this.serviceDeferral != null)
            {
                //Complete the service deferral
                this.serviceDeferral.Complete();
            }
        }
    }
}
