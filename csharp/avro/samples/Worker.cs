﻿//Copyright (c) Microsoft Corporation. All rights reserved.
//Copyright 2016-2017 Confluent Inc., 2015-2016 Andreas Heider
//Licensed under the MIT License.
//Licensed under the Apache License, Version 2.0
//
//Original Confluent sample modified for use with Azure Event Hubs for Apache Kafka Ecosystems

using System;
using System.Collections.Generic;
using System.Configuration;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Azure.Identity;
using com.azure.schemaregistry.samples;
using Confluent.Kafka;
using Microsoft.Azure.Kafka.SchemaRegistry.Avro;

namespace EventHubsForKafkaSample
{
    class Worker
    {
        static readonly string schemaRegistryUrl = ConfigurationManager.AppSettings["SCHEMA_REGISTRY_URL"];
        static readonly string schemaGroup = ConfigurationManager.AppSettings["SCHEMA_GROUP"];
        static readonly ClientSecretCredential credential = new ClientSecretCredential(
                        ConfigurationManager.AppSettings["SCHEMA_REGISTRY_TENANT_ID"],
                        ConfigurationManager.AppSettings["SCHEMA_REGISTRY_CLIENT_ID"],
                        ConfigurationManager.AppSettings["SCHEMA_REGISTRY_CLIENT_SECRET"]);

        public static async Task Producer(string brokerList, string connStr, string topic, string cacertlocation)
        {
            try
            {
                var config = new ProducerConfig
                {
                    BootstrapServers = brokerList,
                    SecurityProtocol = SecurityProtocol.SaslSsl,
                    SaslMechanism = SaslMechanism.Plain,
                    SaslUsername = "$ConnectionString",
                    SaslPassword = connStr,
                    SslCaLocation = cacertlocation,
                    //Debug = "security,broker,protocol"        //Uncomment for librdkafka debugging information
                };

                var valueSerializer = new KafkaAvroSerializer<CustomerInvoice>(
                    schemaRegistryUrl, 
                    credential,
                    schemaGroup,
                    autoRegisterSchemas: true);

                using (var producer = new ProducerBuilder<long, CustomerInvoice>(config).SetKeySerializer(Serializers.Int64).SetValueSerializer(valueSerializer).Build())
                {
                    for (int x = 0; x < 10; x++)
                    {
                        var invoice = new CustomerInvoice()
                        {
                            InvoiceId = "something",
                            MerchantId = "arthur",
                            TransactionValueUsd = 100,
                            UserId = "alice"
                        };
                        var deliveryReport = await producer.ProduceAsync(topic, new Message<long, CustomerInvoice> { Key = DateTime.UtcNow.Ticks, Value = invoice });
                    }
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(string.Format("Exception Occurred - {0}", e.Message));
            }
        }

        public static void Consumer(string brokerList, string connStr, string consumergroup, string topic, string cacertlocation)
        {
            var config = new ConsumerConfig
            {
                BootstrapServers = brokerList,
                SecurityProtocol = SecurityProtocol.SaslSsl,
                SocketTimeoutMs = 60000,                //this corresponds to the Consumer config `request.timeout.ms`
                SessionTimeoutMs = 30000,
                SaslMechanism = SaslMechanism.Plain,
                SaslUsername = "$ConnectionString",
                SaslPassword = connStr,
                SslCaLocation = cacertlocation,
                GroupId = consumergroup,
                AutoOffsetReset = AutoOffsetReset.Earliest,
                BrokerVersionFallback = "1.0.0",        //Event Hubs for Kafka Ecosystems supports Kafka v1.0+, a fallback to an older API will fail
                //Debug = "security,broker,protocol"    //Uncomment for librdkafka debugging information
            };

            var valueDeserializer = new KafkaAvroDeserializer<CustomerInvoice>(schemaRegistryUrl, credential);

            using (var consumer = new ConsumerBuilder<long, CustomerInvoice>(config).SetKeyDeserializer(Deserializers.Int64).SetValueDeserializer(valueDeserializer).Build())
            {
                CancellationTokenSource cts = new CancellationTokenSource();
                Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

                consumer.Subscribe(topic);

                Console.WriteLine("Consuming messages from topic: " + topic + ", broker(s): " + brokerList);

                while (true)
                {
                    try
                    {
                        var msg = consumer.Consume(cts.Token);
                        Console.WriteLine($"Received: '{msg.Value.InvoiceId}'");
                    }
                    catch (ConsumeException e)
                    {
                        Console.WriteLine($"Consume error: {e.Error.Reason}");
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine($"Error: {e.Message}");
                    }
                }
            }
        }
    }
}
