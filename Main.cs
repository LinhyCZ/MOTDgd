using Quobject.SocketIoClientDotNet.Client;
using Rocket.Core.Plugins;
using Rocket.API;
using Rocket.Core.Logging;
using System.IO;
using System;
using Rocket.Unturned;
using Rocket.Unturned.Player;
using Rocket.Unturned.Chat;
using System.Net;
using System.Xml;
using System.Web;
using Steamworks;
using System.Collections.Generic;
using System.Timers;

namespace MOTDgd
{

    public class MOTDgdConfiguration : IRocketPluginConfiguration
    {
        public int User_ID;
        public bool AdvancedLogging;
        public string GiveItems;
        public int CooldownTime;

        public void LoadDefaults()
        {
            User_ID = 0;
            AdvancedLogging = false;
            GiveItems = "";
            CooldownTime = 15;
        }

    }

    //TEST
    public class Main : RocketPlugin<MOTDgdConfiguration>
    {
        //Setting up variables
        public static int Server_ID;
        public static bool Connected;
        public static Dictionary<CSteamID, long> Cooldown = new Dictionary<CSteamID, long>();
        private Timer cooldownTimer;
        public static string User_ID;
        public static Main Instance;

        protected override void Load()
        {
            Instance = this;
            //Creating socket connection
            var socket = IO.Socket("http://mcnode.motdgd.com:8080");
            Logger.Log("Connecting to HUB");

            //Logging in to node
            socket.On("connect", () =>
            {
                Logger.Log("Connected to HUB");
                socket.Emit("login", new Object[0]);
                Connected = true;
            });

            //Reading Server ID
            socket.On("login_response", (arguments) =>
            {
                string login_data = arguments + "";
                int.TryParse(login_data, out Server_ID);
                Logger.Log("Received ID " + Server_ID + " from the HUB");
            });

            //Getting names of people that completed Advertisement
            socket.On("complete_response", (arguments) =>
            {
                string resp_data = arguments + "";
                UnturnedPlayer currentPlayer = getPlayer(resp_data);
                if (currentPlayer != null)
                {
                    if (Configuration.Instance.AdvancedLogging == true)
                    {
                        if (!OnCooldown(currentPlayer))
                        {
                            Logger.Log("User " + currentPlayer.DisplayName + " completed advertisement.");
                        }
                        else
                        {
                            Logger.Log("User " + currentPlayer.DisplayName + " completed advertisement, but is on cooldown");
                        }
                    }

                    if (!OnCooldown(currentPlayer))
                    {
                        GiveReward(currentPlayer);
                        var CooldownTime = CurrentTime.Millis + (Configuration.Instance.CooldownTime * 60 * 1000);
                        Cooldown.Add(currentPlayer.CSteamID, CooldownTime);
                    }
                    else
                    {
                        UnturnedChat.Say(currentPlayer, "You already received reward and now are on cooldown!");
                    }

                }
                else
                {
                    Logger.LogWarning("Player with CSteamID " + resp_data + " completed advertisement but is not on the server.");
                }
            });

            //Disconnecting from node
            socket.On("disconnect", () =>
            {
                Logger.LogWarning("Disconnected");
                Server_ID = 0;
                Connected = false;
            });

            //Telling player about rewards
            U.Events.OnPlayerConnected += (UnturnedPlayer player) =>
            {
                if (Connected == true && !OnCooldown(player))
                {
                    UnturnedChat.Say(player, "For getting reward go to: " + ShortenUrl("http://motdgd.com/motd/?user=" + Configuration.Instance.User_ID + "&gm=minecraft&clt_user=" + player.CSteamID + "&srv_id=" + Server_ID));                  
                }
            };

            //Timer checking Cooldown players
            cooldownTimer = new System.Timers.Timer();
            cooldownTimer.Elapsed += new ElapsedEventHandler(timerFunc);
            cooldownTimer.Interval = 2000;
            cooldownTimer.Enabled = true;
        }


        //Converting URL to shorter vesion
        public static string ShortenUrl(string url) {
            string shortUrl = string.Empty;

            using (WebClient wb = new WebClient())
            {
                string data = string.Format("http://api.bitly.com/v3/shorten/?login={0}&apiKey={1}&longUrl={2}&format={3}",
                "linhycz",
                "R_c3c1e8f0c9264a3ea072786226e37be5",
                HttpUtility.UrlEncode(url),
                "xml");

                XmlDocument xmlDoc = new XmlDocument();
                xmlDoc.LoadXml(wb.DownloadString(data));

                shortUrl = xmlDoc.GetElementsByTagName("url")[0].InnerText;
                return shortUrl;
            }
        }

        //Get player variable from received CSteamID
        public UnturnedPlayer getPlayer(string id)
        {
            CSteamID new_ID = (CSteamID)UInt64.Parse(id);
            UnturnedPlayer player = UnturnedPlayer.FromCSteamID(new_ID);
            return player;
        }

        //Give Reward
        public void GiveReward (UnturnedPlayer player)
        {

            string[] itemList = Configuration.Instance.GiveItems.Split(';');
            foreach (string Item in itemList) {
                if (!Item.ToLower().Contains("heal"))
                {
                    //Give items
                    string[] details = Item.Split(' ');
                    ushort id = Convert.ToUInt16(details[0]);
                    byte count = Convert.ToByte(details[1]);
                    player.GiveItem(id, count);
                }
                else if (Item.ToLower().Contains("heal"))
                {
                    //Heal (how much HP to heal)
                    string[] details = Item.Split(' ');
                    byte amount;
                    byte.TryParse(details[1], out amount);
                    player.Heal(amount);
                }
                else
                {
                    Logger.LogError("Error giving items to player! Format of configuration is incorrect!");
                }
            }
            UnturnedChat.Say(player, "You got your reward! Now you are on cooldown for " + Configuration.Instance.CooldownTime + " minutes.");
        }

        private void timerFunc(object sender, EventArgs e)
        {
            RemoveCooldownLoop();
        }

        //Loop checking cooldown list and removing players after cooldown expiry 
        public void RemoveCooldownLoop()
        {
            foreach (var pair in Cooldown)
            {
                var key = pair.Key;
                var value = pair.Value;
                var currentTime = CurrentTime.Millis;

                if (value <= currentTime)
                {
                    Cooldown.Remove(key);
                    UnturnedPlayer player = UnturnedPlayer.FromCSteamID(key);
                    UnturnedChat.Say(player, "Your cooldown now expired!");
                }
            }
        }

        //Find if in Cooldown
        public static bool OnCooldown(UnturnedPlayer player)
        {
            if (Cooldown.ContainsKey(player.CSteamID))
            {
                return true;
            }
            else
            {
                return false;
            }
        }

        //Return time in Millis since 1.1.1970
        static class CurrentTime
        {
            private static readonly DateTime Jan1St1970 = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            public static long Millis { get { return (long)((DateTime.UtcNow - Jan1St1970).TotalMilliseconds); } }
        }

        //Return cooldown time
        public static string CooldownTime(UnturnedPlayer player)
        {
            foreach (var pair in Cooldown)
            {
                var key = pair.Key;
                var value = pair.Value;
                var currentTime = CurrentTime.Millis;

                if (key == player.CSteamID)
                {
                    var milTime = value - currentTime;
                    double time = milTime / 1000;
                    
                    var minutes = Math.Truncate(time / 60);
                    var seconds = time - (minutes * 60);
                    
                    return minutes + " minutes and " + seconds + " seconds";
                };
            }
            return "";
        }
    }
}
