using Discord;
using Discord.Net.Queue;
using Discord.WebSocket;
using HarmonyLib;
using PartyBot.Handlers;
using PartyBot.Helpers;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace PartyBot.Services
{
    internal class PatchService
    {
        private PatchService() { }

        public static Task RunAsync()
        {
            return Task.Run(Run);
        }

        public static void Run()
        {
            var harmony = new Harmony("Annette");

            var type = typeof(DiscordSocketClient).Assembly.GetType("Discord.API.DiscordSocketApiClient");
            var original = AccessTools.Method(type, "SendIdentifyAsync");

            harmony.Patch(original,
                new HarmonyMethod(Reflection.GetMethod<PatchService>(null, "MyPrefix"))
                );

#if DEBUG
            var sendGateway = AccessTools.Method(type, "SendGatewayAsync");

            harmony.Patch(sendGateway,
                new HarmonyMethod(Reflection.GetMethod<PatchService>(null, "SendGatewayIntercept"))
                );
#endif
        }

        public static bool SendGatewayIntercept(object __instance, ref Task __result, object payload, Enum opCode)
        {
            Console.WriteLine(opCode);

            if (opCode.ToString() == "Identify")
            {
                Console.WriteLine(__instance.InvokeVirtual<string>("SerializeJson", new[] { payload }));
                Console.WriteLine(payload);
            }


            return true;
        }

        public static bool MyPrefix(object __instance, ref Task __result, int largeThreshold, int shardID, int totalShards, bool guildSubscriptions, GatewayIntents? gatewayIntents, object presence, RequestOptions options)
        {
            __result = SendIdentifyAsync(__instance, largeThreshold, shardID, totalShards, guildSubscriptions, gatewayIntents, presence, options);
            return false;
        }

        public static async Task SendIdentifyAsync(object __instance, int largeThreshold, int shardID, int totalShards, bool guildSubscriptions, GatewayIntents? gatewayIntents, object /*(UserStatus, bool, long?, GameModel)?*/ presence, RequestOptions options)
        {
            options = typeof(RequestOptions).InvokeStatic<RequestOptions>("CreateOrClone", new object[] { options }, new Type[] { typeof(RequestOptions) });
            var props = new Dictionary<string, string>
            {
                // Only exists in your dreams...
                { "$os",  "Windows 11 Mobile" },
                // Windows 11 Mobile running Android apps!
                { "$browser", "Discord Android" },
                { "$device", "Discord.Net" }
            };
            var msg = new
            {
                token = GlobalData.Config.DiscordToken,
                // This worked with Lily. I don't know why it failed with Ishar.
                //intents = (int)gatewayIntents,
                properties = props,
                large_threshold = largeThreshold,
                guild_subscriptions = true,
                presence = new
                {
                    activities = new object[]
                    {
                        new
                        {
                            name = "Music at Carano",
                            type = 0,
                            browser = "Discord Android"
                        }
                    },
                    status = "online",
                    afk = false
                }
            };

            //if (totalShards > 1)
            //    msg.ShardingParams = new int[] { shardID, totalShards };

            var gatewayBucket = Reflection.FindType("Discord.Net.Queue.GatewayBucket");
            options.SetProperty("BucketId", gatewayBucket.InvokeStatic<object>("Get", new object[] { GatewayBucketType.Identify }).GetValue<object>(gatewayBucket, "Id"));

            //msg.Intents = (int)gatewayIntents;

            //if (presence != null)
            //{
            //    msg.Presence = new StatusUpdateParams
            //    {
            //        Status = presence.Value.Item1,
            //        IsAFK = presence.Value.Item2,
            //        IdleSince = presence.Value.Item3,
            //        Game = presence.Value.Item4,
            //    };
            //}

            var gatewayOpCode = Reflection.FindType("Discord.API.Gateway.GatewayOpCode");
            await __instance.Invoke<Task>(__instance.GetType(), "SendGatewayAsync", new object[] { Enum.Parse(gatewayOpCode, "Identify"), msg, options }, new Type[] { gatewayOpCode, typeof(object), typeof(RequestOptions) });
        }
    }
}
