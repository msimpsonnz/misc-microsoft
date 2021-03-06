﻿using Microsoft.Azure.CosmosDB.BulkExecutor;
using Microsoft.Azure.CosmosDB.BulkExecutor.BulkImport;
using Microsoft.Azure.Documents;
using Microsoft.Azure.Documents.Client;
using Microsoft.Azure.Documents.Linq;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NoSQL.Common.Helpers;

namespace NoSQL.Infrastructure
{
    public class CosmosHelper
    {
        public static async Task RunQueryByProp(DocumentClient _client, IOptions<CosmosConfig> _cosmosConfig, string queryId, bool crossPartition = false)
        {
            var feedOptions = new FeedOptions {
                EnableCrossPartitionQuery = crossPartition,
                MaxDegreeOfParallelism = 256
            };
            var query = _client.CreateDocumentQuery<CosmosDeviceModel>(UriFactory.CreateDocumentCollectionUri(_cosmosConfig.Value.DatabaseName, _cosmosConfig.Value.CollectionName), feedOptions)
                .Where(d => d.Uid == queryId)
                .AsDocumentQuery();
            var timer = Stopwatch.StartNew();
            var response = await query.ExecuteNextAsync();
            timer.Stop();
            Console.WriteLine($"Cross Partition Query by Property Duration: {timer.Elapsed} - RU:{response.RequestCharge}");

        }

        public static async Task RunQueryById(DocumentClient _client, IOptions<CosmosConfig> _cosmosConfig, string queryId, bool crossPartition = false)
        {
            var feedOptions = new FeedOptions
            {
                EnableCrossPartitionQuery = crossPartition,
                MaxDegreeOfParallelism = 256
            };
            var query = _client.CreateDocumentQuery<CosmosDeviceModel>(UriFactory.CreateDocumentCollectionUri(_cosmosConfig.Value.DatabaseName, _cosmosConfig.Value.CollectionName), feedOptions)
                .Where(d => d.Id == queryId)
                .AsDocumentQuery();
            var timer = Stopwatch.StartNew();
            var response = await query.ExecuteNextAsync();
            timer.Stop();
            Console.WriteLine($"Cross Partition Query by Id Duration: {timer.Elapsed} - RU:{response.RequestCharge}");

        }

        public static async Task GetPartitionKeys(DocumentClient _client, IOptions<CosmosConfig> _cosmosConfig)
        {
            // Cleanup on start if set in config.

            DocumentCollection dataCollection = await SetupCosmosCollection(_client, _cosmosConfig);

            // Prepare for bulk import.

            // Creating documents with simple partition key here.
            string partitionKeyProperty = dataCollection.PartitionKey.Paths[0].Replace("/", "");

        }



            public static async Task RunBulkImportAsync(DocumentClient _client, IOptions<CosmosConfig> _cosmosConfig)
        {
            // Cleanup on start if set in config.

            DocumentCollection dataCollection = await SetupCosmosCollection(_client, _cosmosConfig);

            // Prepare for bulk import.

            // Creating documents with simple partition key here.
            string partitionKeyProperty = dataCollection.PartitionKey.Paths[0].Replace("/", "");

            int numberOfDocumentsToGenerate = _cosmosConfig.Value.NumberOfDocumentsToImport;
            int numberOfBatches = _cosmosConfig.Value.NumberOfBatches;
            long numberOfDocumentsPerBatch = (long)Math.Floor(((double)numberOfDocumentsToGenerate) / numberOfBatches);

            // Set retry options high for initialization (default values).
            _client.ConnectionPolicy.RetryOptions.MaxRetryWaitTimeInSeconds = 30;
            _client.ConnectionPolicy.RetryOptions.MaxRetryAttemptsOnThrottledRequests = 9;

            IBulkExecutor bulkExecutor = new BulkExecutor(_client, dataCollection);
            await bulkExecutor.InitializeAsync();

            // Set retries to 0 to pass control to bulk executor.
            _client.ConnectionPolicy.RetryOptions.MaxRetryWaitTimeInSeconds = 0;
            _client.ConnectionPolicy.RetryOptions.MaxRetryAttemptsOnThrottledRequests = 0;

            BulkImportResponse bulkImportResponse = null;
            long totalNumberOfDocumentsInserted = 0;
            double totalRequestUnitsConsumed = 0;
            double totalTimeTakenSec = 0;

            var tokenSource = new CancellationTokenSource();
            var token = tokenSource.Token;

            for (int i = 0; i < numberOfBatches; i++)
            {
                // Generate JSON-serialized documents to import.

                List<string> documentsToImportInBatch = DocumentBatch(partitionKeyProperty, numberOfDocumentsPerBatch, i);

                // Invoke bulk import API.

                var tasks = new List<Task>();

                tasks.Add(Task.Run(async () =>
                {
                    Console.WriteLine(String.Format("Executing bulk import for batch {0}", i));
                    do
                    {
                        try
                        {
                            bulkImportResponse = await bulkExecutor.BulkImportAsync(
                                documents: documentsToImportInBatch,
                                enableUpsert: true,
                                disableAutomaticIdGeneration: true,
                                maxConcurrencyPerPartitionKeyRange: null,
                                maxInMemorySortingBatchSize: null,
                                cancellationToken: token);
                        }
                        catch (DocumentClientException de)
                        {
                            Console.WriteLine("Document _client exception: {0}", de);
                            break;
                        }
                        catch (Exception e)
                        {
                            Console.WriteLine("Exception: {0}", e);
                            break;
                        }
                    } while (bulkImportResponse.NumberOfDocumentsImported < documentsToImportInBatch.Count);

                    Console.WriteLine(String.Format("\nSummary for batch {0}:", i));
                    Console.WriteLine("--------------------------------------------------------------------- ");
                    Console.WriteLine(String.Format("Inserted {0} docs @ {1} writes/s, {2} RU/s in {3} sec",
                        bulkImportResponse.NumberOfDocumentsImported,
                        Math.Round(bulkImportResponse.NumberOfDocumentsImported / bulkImportResponse.TotalTimeTaken.TotalSeconds),
                        Math.Round(bulkImportResponse.TotalRequestUnitsConsumed / bulkImportResponse.TotalTimeTaken.TotalSeconds),
                        bulkImportResponse.TotalTimeTaken.TotalSeconds));
                    Console.WriteLine(String.Format("Average RU consumption per document: {0}",
                        (bulkImportResponse.TotalRequestUnitsConsumed / bulkImportResponse.NumberOfDocumentsImported)));
                    Console.WriteLine("---------------------------------------------------------------------\n ");

                    totalNumberOfDocumentsInserted += bulkImportResponse.NumberOfDocumentsImported;
                    totalRequestUnitsConsumed += bulkImportResponse.TotalRequestUnitsConsumed;
                    totalTimeTakenSec += bulkImportResponse.TotalTimeTaken.TotalSeconds;
                },
                token));

                /*
                tasks.Add(Task.Run(() =>
                {
                    char ch = Console.ReadKey(true).KeyChar;
                    if (ch == 'c' || ch == 'C')
                    {
                        tokenSource.Cancel();
                        Console.WriteLine("\nTask cancellation requested.");
                    }
                }));
                */

                await Task.WhenAll(tasks);
            }

            Console.WriteLine("Overall summary:");
            Console.WriteLine("--------------------------------------------------------------------- ");
            Console.WriteLine(String.Format("Inserted {0} docs @ {1} writes/s, {2} RU/s in {3} sec",
                totalNumberOfDocumentsInserted,
                Math.Round(totalNumberOfDocumentsInserted / totalTimeTakenSec),
                Math.Round(totalRequestUnitsConsumed / totalTimeTakenSec),
                totalTimeTakenSec));
            Console.WriteLine(String.Format("Average RU consumption per document: {0}",
                (totalRequestUnitsConsumed / totalNumberOfDocumentsInserted)));
            Console.WriteLine("--------------------------------------------------------------------- ");

            // Cleanup on finish if set in config.

            if (_cosmosConfig.Value.ShouldCleanupOnFinish)
            {
                Console.WriteLine("Deleting Database {0}", _cosmosConfig.Value.DatabaseName);
                await _client.DeleteDatabaseAsync(UriFactory.CreateDatabaseUri(_cosmosConfig.Value.DatabaseName));
            }

            Console.WriteLine("\nPress any key to exit.");
            Console.ReadKey();
        }

        public static List<string> DocumentBatch(string partitionKeyProperty, long numberOfDocumentsPerBatch, int i)
        {
            List<string> documentsToImportInBatch = new List<string>();

            Console.WriteLine(string.Format("Generating {0} documents to import for batch {1}", numberOfDocumentsPerBatch, i));
            for (int j = 0; j < numberOfDocumentsPerBatch; j++)
            {
                Guid guid = Guid.NewGuid();
                string partitionKeyValue = HashHelper.GetPartitionKey(BitConverter.ToInt32(guid.ToByteArray(), 0));
                string id = guid.ToString();

                documentsToImportInBatch.Add(DocumentHelper.GenerateRandomDocumentString(id, partitionKeyProperty, partitionKeyValue));
            }

            return documentsToImportInBatch;
        }

        public static async Task<DocumentCollection> SetupCosmosCollection(DocumentClient _client, IOptions<CosmosConfig> _cosmosConfig)
        {
            DocumentCollection dataCollection = null;
            try
            {
                if (_cosmosConfig.Value.ShouldCleanupOnStart)
                {
                    Database database = Utils.GetDatabaseIfExists(_client, _cosmosConfig.Value.DatabaseName);
                    if (database != null)
                    {
                        await _client.DeleteDatabaseAsync(database.SelfLink);
                    }

                    Console.WriteLine("Creating database {0}", _cosmosConfig.Value.DatabaseName);
                    database = await _client.CreateDatabaseAsync(new Database { Id = _cosmosConfig.Value.DatabaseName });

                    Console.WriteLine(String.Format("Creating collection {0} with {1} RU/s", _cosmosConfig.Value.CollectionName, _cosmosConfig.Value.CollectionThroughput));
                    dataCollection = await Utils.CreatePartitionedCollectionAsync(_client, _cosmosConfig.Value.DatabaseName, _cosmosConfig.Value.CollectionPartitionKey, _cosmosConfig.Value.CollectionName, _cosmosConfig.Value.CollectionThroughput);
                }
                else
                {
                    dataCollection = Utils.GetCollectionIfExists(_client, _cosmosConfig.Value.DatabaseName, _cosmosConfig.Value.CollectionName);
                    if (dataCollection == null)
                    {
                        throw new Exception("The data collection does not exist");
                    }
                }
            }
            catch (Exception de)
            {
                Console.WriteLine("Unable to initialize, exception message: {0}", de.Message);
                throw;
            }

            return dataCollection;
        }

    }
}