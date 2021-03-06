﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Windows.ApplicationModel.AppService;
using Windows.ApplicationModel.Background;
using Windows.ApplicationModel.Resources.Core;
using Windows.ApplicationModel.VoiceCommands;
using Windows.Storage;
using System.IO;
using System.Net.Http;
using System.Net.Http.Formatting;
using System.Net.Http.Headers;
using System.Text;
using System.Diagnostics;
using Newtonsoft.Json.Linq;
using System.Globalization;
using Windows.System;

namespace WorldPopulation.VoiceCommands
{

    class StringTable
    {
        public string[] ColumnNames { get; set; }
        public string[,] Values { get; set; }
    }
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
                        case "showPastPopulation":
                             var pastCountry = voiceCommand.Properties["country"][0];
                             var pastYear = voiceCommand.Properties["year"][0];
                            //search type is the whole population
                            string pastSearchType = "\"Population. total\"";
                            await SendCompletionMessageForPastPopulation(pastCountry, pastYear, pastSearchType);
                            break;
                        case "showPastWomenPercentage":
                            var pastWomenCountry = voiceCommand.Properties["country"][0];
                            var pastWomenYear = voiceCommand.Properties["year"][0];
                            //search type is the whole population
                            string womenSearchType = "\"Population. female (% of total)\"";
                            await SendCompletionMessageForPastWomenProportion(pastWomenCountry, pastWomenYear, womenSearchType);
                            break;
                        case "showFuturePopulation":
                            var futureCountry = voiceCommand.Properties["country"][0];
                            var futureYear = voiceCommand.Properties["year"][0];
                            await SendCompletionMessageForFuturePopulation(futureCountry, futureYear);
                            break;
                        case "showFuturePopulationML":
                            var futureYearML = voiceCommand.Properties["year"][0];
                            await SendCompletionMessageForFuturePopulationML(futureYearML);
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

        //Search for the requested data (population from the past) and give a response in cortana
        private async Task SendCompletionMessageForPastPopulation(string country, string year, string searchType)
        {
            // If this operation is expected to take longer than 0.5 seconds, the task must
            // provide a progress response to Cortana prior to starting the operation, and
            // provide updates at most every 5 seconds.
            string calculatingPopulation = string.Format(
                       cortanaResourceMap.GetValue("CalculatingPopulation", cortanaContext).ValueAsString,
                       country, year);
            await ShowProgressScreen(calculatingPopulation);

            //this var will be filled with the according response data from the following REST Call
            var result = await InvokeRequestResponseService(country, year, searchType);
            string population = Convert.ToDouble(result).ToString("#,##,, Million", CultureInfo.InvariantCulture);

            var userMessage = new VoiceCommandUserMessage();
            var responseContentTile = new VoiceCommandContentTile();

            //set the type of the ContentTyle
            responseContentTile.ContentTileType = VoiceCommandContentTileType.TitleWithText;

            //fill the responseContentTile with the data we got
            responseContentTile.AppLaunchArgument = country;
            responseContentTile.Title = country + " " + year;

            responseContentTile.TextLine1 = "Population: " + result;

            //the VoiceCommandResponse needs to be a list
            var tileList = new List<VoiceCommandContentTile>();
            tileList.Add(responseContentTile);

            // Set a message for the Response Cortana Page
            string message = String.Format(cortanaResourceMap.GetValue("ShowPopulation", cortanaContext).ValueAsString, country, year, population);

            userMessage.DisplayMessage = message;
            userMessage.SpokenMessage = message;

            var response = VoiceCommandResponse.CreateResponse(userMessage, tileList);

            //general infos
            await Launcher.LaunchUriAsync(new Uri(@"https://app.powerbi.com/groups/me/dashboards/1e13afdf-70f8-4d7c-b4f5-c95499802d44"));

            //country info
            await Launcher.LaunchUriAsync(new Uri(@"https://app.powerbi.com/groups/me/reports/6ae73462-1d4b-4bb7-928f-75d23fc6bc84/ReportSection?filter=World/Country eq '" + country + "'"));

            await voiceServiceConnection.ReportSuccessAsync(response);
            
        }

        //Search for the requested data (women proportion) and give a response in cortana
        private async Task SendCompletionMessageForPastWomenProportion(string country, string year, string searchType)
        {
            // If this operation is expected to take longer than 0.5 seconds, the task must
            // provide a progress response to Cortana prior to starting the operation, and
            // provide updates at most every 5 seconds.
            string calculatingWomenProportion = string.Format(
                       cortanaResourceMap.GetValue("CalculatingWomenProportion", cortanaContext).ValueAsString,
                       country, year);
            await ShowProgressScreen(calculatingWomenProportion);

            //this var will be filled with the according response data from the following REST Call
            var result = await InvokeRequestResponseService(country, year, searchType);
            string womenProportion = Convert.ToDouble(result).ToString("#.##")+"%";

            var userMessage = new VoiceCommandUserMessage();
            var responseContentTile = new VoiceCommandContentTile();

            //set the type of the ContentTyle
            responseContentTile.ContentTileType = VoiceCommandContentTileType.TitleWithText;

            //fill the responseContentTile with the data we got
            responseContentTile.AppLaunchArgument = country;
            responseContentTile.Title = country + " " + year;

            responseContentTile.TextLine1 = "Women proportion: " + womenProportion;

            //the VoiceCommandResponse needs to be a list
            var tileList = new List<VoiceCommandContentTile>();
            tileList.Add(responseContentTile);

            // Set a message for the Response Cortana Page
            string message = String.Format(cortanaResourceMap.GetValue("ShowWomenProportion", cortanaContext).ValueAsString, country, year, womenProportion);

            userMessage.DisplayMessage = message;
            userMessage.SpokenMessage = message;

            var response = VoiceCommandResponse.CreateResponse(userMessage, tileList);

            //general infos
            await Windows.System.Launcher.LaunchUriAsync(new Uri(@"https://app.powerbi.com/groups/me/dashboards/1e13afdf-70f8-4d7c-b4f5-c95499802d44"));

            //women proportion 
            await Windows.System.Launcher.LaunchUriAsync(new Uri(@"https://app.powerbi.com/groups/me/reports/16a72348-d0d6-4add-989e-aad4cf560073/ReportSection?filter=World/Year eq '"+year+"'"));

            await voiceServiceConnection.ReportSuccessAsync(response);
        }

        //Search for the requested data (future population) and give a response in cortana
        private async Task SendCompletionMessageForFuturePopulation(string country, string year)
        {
            // If this operation is expected to take longer than 0.5 seconds, the task must
            // provide a progress response to Cortana prior to starting the operation, and
            // provide updates at most every 5 seconds.
            string calculatingFuturePopulation = string.Format(
                       cortanaResourceMap.GetValue("CalculatingPopulation", cortanaContext).ValueAsString,
                       country, year);
            await ShowProgressScreen(calculatingFuturePopulation);

            //this var will be filled with the according response data from the following REST Call
            var result = await InvokeRequestResponseServicePredictive(country, year);
            string population = Convert.ToDouble(result).ToString("#,##,, Million", CultureInfo.InvariantCulture);

            var userMessage = new VoiceCommandUserMessage();
            var responseContentTile = new VoiceCommandContentTile();

            //set the type of the ContentTyle
            responseContentTile.ContentTileType = VoiceCommandContentTileType.TitleWithText;

            //fill the responseContentTile with the data we got
            responseContentTile.AppLaunchArgument = country;
            responseContentTile.Title = country + " " + year;

            responseContentTile.TextLine1 = "Population: " + result;

            //the VoiceCommandResponse needs to be a list
            var tileList = new List<VoiceCommandContentTile>();
            tileList.Add(responseContentTile);

            // Set a message for the Response Cortana Page
            string message = String.Format(cortanaResourceMap.GetValue("ShowFuturePopulation", cortanaContext).ValueAsString, country, year, population);

            userMessage.DisplayMessage = message;
            userMessage.SpokenMessage = message;

            var response = VoiceCommandResponse.CreateResponse(userMessage, tileList);

            //general infos
            await Windows.System.Launcher.LaunchUriAsync(new Uri(@"https://app.powerbi.com/groups/me/dashboards/1e13afdf-70f8-4d7c-b4f5-c95499802d44"));

            //country info
            await Windows.System.Launcher.LaunchUriAsync(new Uri(@"https://app.powerbi.com/groups/me/reports/6ae73462-1d4b-4bb7-928f-75d23fc6bc84/ReportSection?filter=World/Country eq '" + country + "'"));


            await voiceServiceConnection.ReportSuccessAsync(response);
        }

        //Search for the requested data (future population) and give a response in cortana
        private async Task SendCompletionMessageForFuturePopulationML(string year)
        {
            // If this operation is expected to take longer than 0.5 seconds, the task must
            // provide a progress response to Cortana prior to starting the operation, and
            // provide updates at most every 5 seconds.
            string calculatingFuturePopulation = string.Format(
                       cortanaResourceMap.GetValue("CalculatingPopulation", cortanaContext).ValueAsString,
                       "Germany", year);
            await ShowProgressScreen(calculatingFuturePopulation);

            //this var will be filled with the according response data from the following REST Call
            var result = await InvokeRequestResponseServicePredictiveML(year);
            string population = Convert.ToDouble(result).ToString("#,##,, Million", CultureInfo.InvariantCulture);

            var userMessage = new VoiceCommandUserMessage();
            var responseContentTile = new VoiceCommandContentTile();

            //set the type of the ContentTyle
            responseContentTile.ContentTileType = VoiceCommandContentTileType.TitleWithText;

            //fill the responseContentTile with the data we got
            responseContentTile.AppLaunchArgument = "Germany";
            responseContentTile.Title = "Germany" + " " + year;

            responseContentTile.TextLine1 = "Population: " + Math.Round(Convert.ToDouble(result),2).ToString();

            //the VoiceCommandResponse needs to be a list
            var tileList = new List<VoiceCommandContentTile>();
            tileList.Add(responseContentTile);

            // Set a message for the Response Cortana Page
            string message = String.Format(cortanaResourceMap.GetValue("ShowFuturePopulation", cortanaContext).ValueAsString, "Germany", year, population);

            userMessage.DisplayMessage = message;
            userMessage.SpokenMessage = message;

            var response = VoiceCommandResponse.CreateResponse(userMessage, tileList);

            //general infos
            await Windows.System.Launcher.LaunchUriAsync(new Uri(@"https://app.powerbi.com/groups/me/dashboards/1e13afdf-70f8-4d7c-b4f5-c95499802d44"));

            //country info
            await Windows.System.Launcher.LaunchUriAsync(new Uri(@"https://app.powerbi.com/groups/me/reports/6ae73462-1d4b-4bb7-928f-75d23fc6bc84/ReportSection?filter=World/Country eq '" + "Germany" + "'"));


            await voiceServiceConnection.ReportSuccessAsync(response);
        }

        // REST API call 
        private  async Task<string> InvokeRequestResponseService(string country, string year, string searchType)
        {
            //call REST API from Azure Cloud which offers the needed data
            //country and year are parameters for the SQLite Query
            using (var client = new HttpClient())
            {
                //build the JSON payload
                string sqlParam = "select \"Country Name\", \"" + year + "\" from t1 where \"Country Name\" LIKE '" + country + "' AND \"Indicator Name\" LIKE"+searchType+" ;";
                var scoreRequest = new
                {

                    Inputs = new Dictionary<string, StringTable>() {
                        {
                            "input1",
                            new StringTable()
                            {
                                ColumnNames = new string[] {"Country", "Year", "SearchType"},
                                Values = new string[,] {  { "value", "0", "value" },  { "value", "0", "value" },  }
                            }
                        },
                    },
                    GlobalParameters = new Dictionary<string, string>() {
                                     { "SQL Query Script",sqlParam},
                                }
                };

                //API Key for the web service
                const string apiKey = "8KICXycSV+ngjQhhAcV07NL53ojtjV0QV6ppwCmYov3fR/il9GVuSz4CiDeRk2t+AGLUv9KmcYGrmxSCDoqGJA==";

                //add the key to the header
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

                client.BaseAddress = new Uri("https://europewest.services.azureml.net/workspaces/bd19930a2188458ba118733fdec7d7b0/services/bac09f8efed048709984eb336df4647d/execute?api-version=2.0&details=true");

                // WARNING: The 'await' statement below can result in a deadlock if you are calling this code from the UI thread of an ASP.Net application.
                // One way to address this would be to call ConfigureAwait(false) so that the execution does not attempt to resume on the original context.
                // For instance, replace code such as:
                //      result = await DoSomeTask()
                // with the following:
                //      result = await DoSomeTask().ConfigureAwait(false)


                HttpResponseMessage httpResponse = await client.PostAsJsonAsync("", scoreRequest);
                var returnValue = "";
                if (httpResponse.IsSuccessStatusCode)
                {
                    //get the resultstring, parse it to an JObject,exctract the population from it and store it in population var
                    string result = await httpResponse.Content.ReadAsStringAsync();
                    JObject json = JObject.Parse(result);
                    returnValue = json["Results"]["output1"]["value"]["Values"][0][1].ToString();
                    
                }
                else
                {
                    Debug.WriteLine(string.Format("The request failed with status code: {0}", httpResponse.StatusCode));

                    // Print the headers - they include the requert ID and the timestamp, which are useful for debugging the failure
                    Debug.WriteLine(httpResponse.Headers.ToString());

                    string responseContent = await httpResponse.Content.ReadAsStringAsync();
                    Debug.WriteLine(responseContent);
                }
                return (returnValue);
            }
        }

        private async Task<string> InvokeRequestResponseServicePredictive(string country, string year)
        {
            //call REST API from Azure Cloud which offers the needed data
            //country and year are parameters for the SQLite Query
            using (var client = new HttpClient())
            {
                //build the JSON payload
                string sqlParam = "select \"2005\", \"2006\", \"2007\", \"2008\", \"2009\", \"2010\", \"2011\", \"2012\", \"2013\", \"2014\", \"2015\" from t1 where \"Country Name\" LIKE '" + country + "' AND \"Indicator Name\" LIKE 'Population. total';";
                var scoreRequest = new
                {

                    Inputs = new Dictionary<string, StringTable>() {
                        {
                            "input1",
                            new StringTable()
                            {
                                ColumnNames = new string[] {"number", "country", "year", "searchType"},
                                Values = new string[,] {  { "0", "value", "0", "value" },  { "0", "value", "0", "value" },  }
                            }
                        },
                    },
                    GlobalParameters = new Dictionary<string, string>() {
        { "SQL Query Script", sqlParam },
        { "Data", year },
                    }
                };
                //API Key for the web service
                const string apiKey = "XNa5xuObsesgxS3Gl2g618j9eEh6ant94hWhDSN5cs92dQikzxjhZfj2bNCsxKsOTGo8VOI4ybLS+Cka9ZEZnQ==";

                //add the key to the header
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

                client.BaseAddress = new Uri("https://europewest.services.azureml.net/workspaces/bd19930a2188458ba118733fdec7d7b0/services/c8e69fce4f6a4c709ae8b30fe33cc661/execute?api-version=2.0&details=true");

                // WARNING: The 'await' statement below can result in a deadlock if you are calling this code from the UI thread of an ASP.Net application.
                // One way to address this would be to call ConfigureAwait(false) so that the execution does not attempt to resume on the original context.
                // For instance, replace code such as:
                //      result = await DoSomeTask()
                // with the following:
                //      result = await DoSomeTask().ConfigureAwait(false)


                HttpResponseMessage httpResponse = await client.PostAsJsonAsync("", scoreRequest);
                var returnValue = "";
                if (httpResponse.IsSuccessStatusCode)
                {
                    //get the resultstring, parse it to an JObject,exctract the population from it and store it in population var
                    string result = await httpResponse.Content.ReadAsStringAsync();
                    JObject json = JObject.Parse(result);
                    returnValue = json["Results"]["output1"]["value"]["Values"][0][0].ToString();

                }
                else
                {
                    Debug.WriteLine(string.Format("The request failed with status code: {0}", httpResponse.StatusCode));

                    // Print the headers - they include the requert ID and the timestamp, which are useful for debugging the failure
                    Debug.WriteLine(httpResponse.Headers.ToString());

                    string responseContent = await httpResponse.Content.ReadAsStringAsync();
                    Debug.WriteLine(responseContent);
                }
                return (returnValue);
            }
        }

        private async Task<string> InvokeRequestResponseServicePredictiveML(string year)
        {
            //call REST API from Azure Cloud which offers the needed data
            //country and year are parameters for the SQLite Query
            using (var client = new HttpClient())
            {
                var scoreRequest = new
                {

                    Inputs = new Dictionary<string, StringTable>() {
                        {
                            "input1",
                            new StringTable()
                            {
                                ColumnNames = new string[] {"Column 0", "Germany Population total"},
                                Values = new string[,] {  { year, "0" },  { "0", "0" },  }
                            }
                        },
                    },
                    GlobalParameters = new Dictionary<string, string>() {
        { "Data", "" },
}
                };
                //API Key for the web service
                const string apiKey = "3WPcGhCWSmV3KpueJYp7zFfHK7GCnUGqTBRVBy0Lyc3gfhIkrMgunhnaaK3VFfwXAU6x5uVtLnpJrki3oPCO3Q==";

                //add the key to the header
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

                client.BaseAddress = new Uri("https://ussouthcentral.services.azureml.net/workspaces/54f97b09718944f59e75a0de95c47ff6/services/0ab6460461a347ee8aa4c1a3546c5500/execute?api-version=2.0&details=true");

                // WARNING: The 'await' statement below can result in a deadlock if you are calling this code from the UI thread of an ASP.Net application.
                // One way to address this would be to call ConfigureAwait(false) so that the execution does not attempt to resume on the original context.
                // For instance, replace code such as:
                //      result = await DoSomeTask()
                // with the following:
                //      result = await DoSomeTask().ConfigureAwait(false)


                HttpResponseMessage httpResponse = await client.PostAsJsonAsync("", scoreRequest);
                var returnValue = "";
                if (httpResponse.IsSuccessStatusCode)
                {
                    //get the resultstring, parse it to an JObject,exctract the population from it and store it in population var
                    string result = await httpResponse.Content.ReadAsStringAsync();
                    JObject json = JObject.Parse(result);
                    returnValue = json["Results"]["output1"]["value"]["Values"][0][0].ToString();

                }
                else
                {
                    Debug.WriteLine(string.Format("The request failed with status code: {0}", httpResponse.StatusCode));

                    // Print the headers - they include the requert ID and the timestamp, which are useful for debugging the failure
                    Debug.WriteLine(httpResponse.Headers.ToString());

                    string responseContent = await httpResponse.Content.ReadAsStringAsync();
                    Debug.WriteLine(responseContent);
                }
                return (returnValue);
            }
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
