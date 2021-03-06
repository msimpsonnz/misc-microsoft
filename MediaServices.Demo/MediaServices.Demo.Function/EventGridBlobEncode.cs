// Default URL for triggering event grid function in the local environment.
// http://localhost:7071/runtime/webhooks/EventGridExtensionConfig?functionName={functionname}

//Ported Media Services functions from 
//https://github.com/Azure-Samples/media-services-v3-dotnet-tutorials/blob/master/AMSV3Tutorials/UploadEncodeAndStreamFiles/Program.cs
// Credit to @Juliako, @johndeu and @mconverti

using Microsoft.Azure.Management.Media;
using Microsoft.Azure.Management.Media.Models;
using Microsoft.Azure.Services.AppAuthentication;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.EventGrid;
using Microsoft.Extensions.Logging;
using Microsoft.Rest;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MediaServices.Demo.Function.Helpers;

namespace MediaServices.Demo.Function
{
    public static class EventGridBlobEncode
    {
        private static readonly string ArmEndpoint = Environment.GetEnvironmentVariable("ArmEndpoint");
        private static readonly string SubscriptionId = Environment.GetEnvironmentVariable("SubscriptionId");
        private static readonly string ResourceGroup = Environment.GetEnvironmentVariable("resourceGroup");
        private static readonly string AccountName = Environment.GetEnvironmentVariable("accountName");
        private static readonly string TransformName = Environment.GetEnvironmentVariable("TransformName");
        private static readonly string inputBlobSAS = Environment.GetEnvironmentVariable("inputBlobSAS");
        private static readonly string testBlob = Environment.GetEnvironmentVariable("testBlob");

        [FunctionName("EventGridBlobEncode")]
        public static async Task Run([EventGridTrigger]JObject eventGridEvent, ILogger log)
        {
            log.LogInformation($"EventGrid: {eventGridEvent}");
            string correlationId = eventGridEvent["id"].ToString();

            //Parse blob Uri
            string blobUri = eventGridEvent["data"]["url"].ToString();
            //Get the full blob name
            string fileFull = FileHelper.GetFullFileName(blobUri);
            //Get the blob name without the extension
            var fileId = FileHelper.GetFileNameWithoutExt(fileFull);

            //Build variables to ensure AMS Assests and Jobs are unique
            string jobName = $"job-{fileId}";
            string outputAssetName = $"output-{fileId}";

            try
            {
                //Get MSI Token from Function Host
                var azureServiceTokenProvider = new AzureServiceTokenProvider();
                string accessToken = await azureServiceTokenProvider.GetAccessTokenAsync(ArmEndpoint);
                ServiceClientCredentials credentials = new TokenCredentials(accessToken);

                //Create Media Services Client
                IAzureMediaServicesClient client = CreateMediaServicesClientAsync(credentials);

                //Ensuure Transform Exists
                Transform transform = await GetOrCreateTransformAsync(client, TransformName);

                //Create output assest for the jon
                Asset outputAsset = await CreateOutputAssetAsync(client, outputAssetName);

                //Parse Blob URI from Event data to pass to AMS Job
                var inputBlobName = $"{eventGridEvent["data"]["url"].ToString()}?{inputBlobSAS}";

                //Get Source File
                var meta = VideoInfo.BlobVideoInfo(inputBlobName, correlationId, log);

                //Add outputAsset to metadata
                meta.Add("outputAssetId", outputAsset.AssetId.ToString());

                //Submit AMS Job
                await SubmitJobAsync(inputBlobName, outputAssetName, client, jobName, log, meta);

            }
            catch (Exception ex)
            {
                log.LogError($"Error with Function init: {ex.Message}");
                throw;
            }

        }

        private static async Task<Job> SubmitJobAsync(string inputBlobName, string outputAssetName, IAzureMediaServicesClient client, string jobName, ILogger log, Dictionary<string, string> metadata)
        {

            try
            {
                JobInputHttp jobInput = new JobInputHttp(files: new[] { inputBlobName });
                JobOutput[] jobOutputs = { new JobOutputAsset(outputAssetName) };

                var metaString = JsonConvert.SerializeObject(metadata);
                Dictionary<string, string> metaDic = new Dictionary<string, string>();
                metaDic.Add("metaString", metaString);
                // In this example, we are assuming that the job name is unique.
                //
                // If you already have a job with the desired name, use the Jobs.Get method
                // to get the existing job. In Media Services v3, Get methods on entities returns null 
                // if the entity doesn't exist (a case-insensitive check on the name).
                Job job = await client.Jobs.CreateAsync(
                    ResourceGroup,
                    AccountName,
                    TransformName,
                    jobName,
                    new Job
                    {   
                        CorrelationData = metaDic,
                        Input = jobInput,
                        Outputs = jobOutputs,
                    });

                return job;
            }
            catch (Exception ex)
            {
                log.LogError($"Error with creating Job: {ex.Message}");
                throw;
            }


        }

        private static async Task<Asset> CreateOutputAssetAsync(IAzureMediaServicesClient client, string assetName)
        {
            // Check if an Asset already exists
            Asset outputAsset = await client.Assets.GetAsync(ResourceGroup, AccountName, assetName);
            Asset asset = new Asset();
            string outputAssetName = assetName;
            return await client.Assets.CreateOrUpdateAsync(ResourceGroup, AccountName, outputAssetName, asset);
        }

        private static async Task<Transform> GetOrCreateTransformAsync(IAzureMediaServicesClient client, string transformName)
        {
            // Does a Transform already exist with the desired name? Assume that an existing Transform with the desired name
            // also uses the same recipe or Preset for processing content.
            Transform transform = await client.Transforms.GetAsync(ResourceGroup, AccountName, transformName);

            if (transform == null)
            {
                // You need to specify what you want it to produce as an output
                TransformOutput[] outputs = new TransformOutput[]
                {
                    new TransformOutput(new BuiltInStandardEncoderPreset(EncoderNamedPreset.AdaptiveStreaming)),
                    new TransformOutput(new VideoAnalyzerPreset())
                };


                // Create the Transform with the output defined above
                transform = await client.Transforms.CreateOrUpdateAsync(ResourceGroup, AccountName, transformName, outputs);
            }

            return transform;
        }

        private static IAzureMediaServicesClient CreateMediaServicesClientAsync(ServiceClientCredentials credentials)
        {
            return new AzureMediaServicesClient(new Uri(ArmEndpoint), credentials)
            {
                SubscriptionId = SubscriptionId,
            };
        }
    }

}
