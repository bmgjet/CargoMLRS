/*
 ▄▄▄▄    ███▄ ▄███▓  ▄████  ▄▄▄██▀▀▀▓█████▄▄▄█████▓
▓█████▄ ▓██▒▀█▀ ██▒ ██▒ ▀█▒   ▒██   ▓█   ▀▓  ██▒ ▓▒
▒██▒ ▄██▓██    ▓██░▒██░▄▄▄░   ░██   ▒███  ▒ ▓██░ ▒░
▒██░█▀  ▒██    ▒██ ░▓█  ██▓▓██▄██▓  ▒▓█  ▄░ ▓██▓ ░ 
░▓█  ▀█▓▒██▒   ░██▒░▒▓███▀▒ ▓███▒   ░▒████▒ ▒██▒ ░ 
░▒▓███▀▒░ ▒░   ░  ░ ░▒   ▒  ▒▓▒▒░   ░░ ▒░ ░ ▒ ░░   
▒░▒   ░ ░  ░      ░  ░   ░  ▒ ░▒░    ░ ░  ░   ░    
 ░    ░ ░      ░   ░ ░   ░  ░ ░ ░      ░    ░      
 ░             ░         ░  ░   ░      ░  ░
Permissions: 
MLRSCargo.admin    Required for chat commands

Chat Commands:
/mlrscargotest   -  Give Admin MLRS Air Strike Smoke Signal
/mlrscargoreset  -  Resets the MLRS on all cargoships
*/
using Facepunch;
using Newtonsoft.Json;
using Oxide.Core.Plugins;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
namespace Oxide.Plugins
{
    [Info("MLRSCargo", "bmgjet", "1.0.6")]
    [Description("Places a MLRS on the front of CargoShip")]
    public class MLRSCargo : RustPlugin
    {
        public static MLRSCargo _plugin;
        public List<MLRS> mlrs = new List<MLRS>();
        //Active MRLS to limit to 1 user at a time.
        public static bool active = false;        
        //Last player to trigger event
        public BasePlayer thrower;        
        //Show Debug Info
        bool showDebug = false;
        //Admining Permission
        public const string AdminPerm = "MLRSCargo.admin";

        //Reference Kits plugin so kits can be applied to NPCs
        [PluginReference]
        private Plugin Kits;

        #region Configuration
        private Configuration config;
        private class Configuration
        {
            [JsonProperty("Use Remote Controlled Mode With a Air Strike Signal (Requires you also enabled SpawnReady)")]
            public bool RemoteMode = false;

            [JsonProperty("SpawnReady (MLRS Spawns Ready To Fire)")]
            public bool Ready = false;

            [JsonProperty("Cool Down Between MLRS Attacks")]
            public int Delay = 600;

            //Show announcements in public chat
            [JsonProperty("Show Announcements In Public Chat")]
            public bool PublicAnouncements = false;

            [JsonProperty("Steam ID To Use As Profile Pic")]
            //Steam ID to use profile pic as announcment chat icon
            public string AnnouncementIcon = "76561199219302299";

            //Cargo NPCs health multiplyer
            [JsonProperty("Cargo NPCs Health Multiplyer (Boosts Health Of NPCs With In Radius)")]
            public float healthmulti = 3f;

            [JsonProperty("Cargo NPCs Scan Radius")]
            //Radius to cast NPC Scan
            public float NPCRadius = 50f; //Scan first 1/4 of ship around MLRS

            [JsonProperty("Give Kit To Found NPCs (Leave Empty For Default)")]
            public string Kitname = "";

            [JsonProperty("Play Air Raid Siren On AirStrike Signal")]
            //Play SFX over boombox from remote MLRS grenade
            public  bool PlaySFX = true;

            [JsonProperty("URL To Air Raid Sound Effect (Must Be Raw MP3 Stream)")]
            //URL to SFX (Must be a raw mp3 file)
            public string SFXURL = "https://github.com/bmgjet/RocketFail/blob/main/AirRaid.mp3?raw=true";

            [JsonProperty("MLRS Offset On Cargoship")]
            //Position on cargoship moved from dead center
            public Vector3 CargoOffset = new Vector3(0f, 9.5f, 76f);

            public string ToJson() => JsonConvert.SerializeObject(this);

            public Dictionary<string, object> ToDictionary() => JsonConvert.DeserializeObject<Dictionary<string, object>>(ToJson());
        }

        protected override void LoadDefaultConfig() { config = new Configuration(); }
        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                config = Config.ReadObject<Configuration>();
                if (config == null) { throw new JsonException(); }

                if (!config.ToDictionary().Keys.SequenceEqual(Config.ToDictionary(x => x.Key, x => x.Value).Keys))
                {
                    PrintWarning("Configuration appears to be outdated; updating and saving");
                    SaveConfig();
                }
            }
            catch
            {
                PrintWarning($"Configuration file {Name}.json is invalid; using defaults");
                LoadDefaultConfig();
            }
        }
        protected override void SaveConfig()
        {
            PrintWarning($"Configuration changes saved to {Name}.json");
            Config.WriteObject(config, true);
        }
        #endregion Configuration

        private void Init()
        {
            _plugin = this;
            Unsubscribe("OnEntitySpawned");
            permission.RegisterPermission(AdminPerm, this);
        }

        //Remote control only disable mounting
        object CanMountEntity(BasePlayer bp, BaseMountable bm)
        {
            if (bm.prefabID == 223554808)
            {
                if (config.RemoteMode && config.Ready)
                {
                    //Checks its a MLRS thats apart of the event
                    MLRSShoot s = bm.GetComponent<MLRSShoot>();
                    if (s != null)
                    {
                        //Has token avaliable.
                        if (s.Token)
                        {
                            s.Token = false;
                            //Gives tokwn to player
                            GiveMLRSSignal(bp);
                            //Disable Mount
                            return true;
                        }
                        else
                        {
                            rust.SendChatMessage(bp, "<color=orange>Cargo MLRS</color>", "MLRS signal has already been claimed", config.AnnouncementIcon);
                            //Disable Mount
                            return true;
                        }
                    }
                }
            }
            //Allow Mount
            return null;
        }

        //Loads MLRS on cargo as it spawns
        void OnEntitySpawned(CargoShip cs)
        {
            //checks if cargo
            if (cs != null)
            {
                //Creates MLRS
                timer.Once(10f, () =>
                {
                    AddMLRS(cs);
                });
            }
        }

        void OnEntitySpawned(MLRSRocket rocket)
        {
            //Checks if its one of the events rockets
            if (rocket != null && active)
            {
                if (rocket.OwnerID == 0)
                {
                    //Sets rocket as last user to trigger the event
                    rocket.OwnerID = thrower.userID;
                    rocket.creatorEntity = thrower;
                    //Create map markers since that event was missed with remote trigger.
                    BaseEntity baseEntity2 = GameManager.server.CreateEntity("assets/content/vehicles/mlrs/mlrsrocketmarker.prefab", rocket.transform.position, Quaternion.identity, true);
                    baseEntity2.OwnerID = thrower.userID;
                    baseEntity2.Spawn();
                    baseEntity2.SetParent(rocket, true, false);
                }
            }
        }

        //Prevent Looting Cargos MLRS when SpawnReady Set
        private void OnLootEntity(BasePlayer player, BaseEntity entity)
        {
            try
            {
                if (!entity.GetParentEntity().GetComponent<MLRSShoot>())
                    return;
                if (config.Ready)
                    NextTick(player.EndLooting);
            }
            catch { }
        }

        void OnServerInitialized()
        {
            //Validate Settings
            if (config.Delay < 10)
            {
                Puts("Invalid Delay Must be 10 or greater!");
                config.Delay = 10;
            }
            if (config.RemoteMode && !config.Ready)
            {
                Puts("Spawn Ready Must Be Used With Remote Mode Since CockPit Is Unavaliable!");
                config.Ready = true;
            }
            //try catch since throws errors if server had crashed instead of clean shutdown while cargo is out.
            try
            {
                Subscribe("OnEntitySpawned");
                timer.Once(30,()=>{ResetCargoMLRS(); });
            }
            catch { }
        }

        void Unload()
        {
            //Remove component
            foreach(var ent in mlrs)
            {
                if(ent != null) { continue; }
                if (!ent.IsDestroyed) { ent.Kill(); }
            }
            _plugin = null;
        }

        void OnExplosiveDropped(BasePlayer bp, BaseEntity be)
        {
            if (bp == null || be == null || be.ShortPrefabName != "grenade.smoke.deployed") return;
            RemoteEvent(bp, be);
        }
        void OnExplosiveThrown(BasePlayer bp, BaseEntity be)
        {
            if (bp == null || be == null || be.ShortPrefabName != "grenade.smoke.deployed") return;
            RemoteEvent(bp, be);
        }

        private void OnCargoShipEgress(CargoShip cs)
        {
            if (cs != null)
            {
                for(int i = cs.children.Count - 1; i >=0; i--)
                {
                    if (cs.children[i] != null && !cs.children[i].IsDestroyed && cs.children[i] is MLRS)
                    {
                        cs.children[i].Kill();
                    }
                }
            }
        }

        void RemoteEvent(BasePlayer bp, BaseEntity be)
        {
            //Check if its an event token
            if (be.skinID != 2647098356) return;
            if (showDebug) Puts("Remote Cargo MLRS trigged by " + bp.displayName);

            RunEffect("assets/bundled/prefabs/fx/smoke_signal_full.prefab", be);
            //Checks if already active event
            if ((be != null && !active) || !config.RemoteMode)
            {
                bool Sucess = false;
                thrower = bp;
                active = true;
                if (config.PlaySFX)
                {
                    CreateSound(be, config.SFXURL);
                }
                //delay to allow smoke grenade to travel
                timer.Once(8f, () =>
                {
                    if (be == null)
                    {
                        active = false;
                        rust.SendChatMessage(bp, "<color=orange>Cargo MLRS</color>", "Your MLRS signal <color=red>FAILED</color>", config.AnnouncementIcon);
                        return;
                    }
                    Vector3 pos = be.transform.position;
                    if (pos != null)
                    {
                        if (config.PublicAnouncements)
                        {
                            CreateAnouncment("<color=red>" + bp.displayName + "</color> has called a MLRS strike near <color=orange>" + getGrid(pos) + "</color>");
                        }
                        //Find all cargo ships and use them
                        CargoShip[] cargo = UnityEngine.Object.FindObjectsOfType<CargoShip>();
                        foreach (CargoShip ship in cargo)
                        {
                            //Search if they have MLRS attached
                            List<BaseEntity> bef = ship.children;
                            //Do a for loop incase future has more then 1 MLRS
                            foreach (BaseEntity b in bef.ToArray())
                            {
                                //Found a MLRS
                                if (b is MLRS)
                                {
                                    MLRS TMLRS = b as MLRS;
                                    if (TMLRS != null)
                                    {
                                        //Add the functions componant
                                        MLRSShoot s = TMLRS.GetComponent<MLRSShoot>();
                                        if (s == null)
                                        {
                                            //Check if any shots have been fired in the foreach loop to prevent refunds
                                            if (!Sucess)
                                            {
                                                rust.SendChatMessage(bp, "<color=orange>Cargo MLRS</color>", "<color=red>Not Avaliable</color=red>", config.AnnouncementIcon);
                                                bp.GiveItem(CreateItem());
                                                NextTick(() => { be?.Kill(); });
                                            }
                                            return;
                                        }
                                        //Sets up target and shoots
                                        s.SetTarget(pos);
                                        s.ForceFireRepeat(bp);
                                        if (showDebug) Puts("Remote Cargo MLRS Shooting");
                                        Sucess = true;
                                        timer.Once(10f, () =>
                                        {
                                            if (config.PublicAnouncements)
                                            {
                                                CreateAnouncment("Status: <color=red>Disabled</color>");
                                            }
                                        });
                                        //Resets the cargo after delay
                                        if (config.Ready)
                                        {
                                            timer.Once(10 + config.Delay, () =>
                                            {
                                                ResetCargoMLRS();
                                            });
                                        }
                                    }
                                }
                            }
                        }
                    }
                });
            }
            else
            {
                //Returns players token
                rust.SendChatMessage(bp, "<color=orange>Cargo MLRS</color>", "<color=red>Not Avaliable</color=red>", config.AnnouncementIcon);
                bp.GiveItem(CreateItem());
                NextTick(() => { be?.Kill(); });
            }
        }

        //Find all cargoships on map
        void ResetCargoMLRS()
        {
            CargoShip[] cargo = UnityEngine.Object.FindObjectsOfType<CargoShip>();
            foreach (CargoShip ship in cargo.ToArray())
            {
                //Search if they have MLRS attached
                List<BaseEntity> be = ship.children;
                foreach (BaseEntity b in be.ToArray())
                {
                    if (b is MLRS)
                    {
                        //Kill old one and respawn new to stop mountable spam
                        try
                        {
                            b.Kill();
                        }
                        catch { }
                        AddMLRS(ship);
                    }
                }
            }
        }

        //Removes any duplicated MLRS caused from currupted sav files.
        void CleanDupedMLRS(Vector3 pos)
        {
            int cleaned = 0;
            RaycastHit[] hits = UnityEngine.Physics.SphereCastAll(pos, 150f, Vector3.one);
            foreach (RaycastHit hit in hits.ToArray())
            {
                //Check each hit is a MLRS
                CargoShip cargo = hit.GetEntity()?.GetComponent<CargoShip>();

                if (cargo != null)
                {
                    List<BaseEntity> bef = cargo.children;
                    {
                        foreach (BaseEntity b in bef)
                        {
                            if (b is MLRS)
                            {
                                b.Kill();
                                cleaned++;
                            }
                        }
                    }
                }
            }
            MLRS[] MLRSList = UnityEngine.Object.FindObjectsOfType<MLRS>();
            foreach (MLRS mlrs in MLRSList)
            {
                if (mlrs == null) { continue; }
                if (mlrs.transform.position.y == 0 && TerrainMeta.HeightMap.GetHeight(mlrs.transform.position) < -5)
                {
                    mlrs.Kill();
                    cleaned++;
                }
            }
            if (showDebug) Puts("Cleaned " + cleaned.ToString() + " dupes");
        }

        void AddMLRS(CargoShip cs)
        {
            if (cs == null) { return; }
            CleanDupedMLRS(cs.transform.position + config.CargoOffset);
            //Rotate MLRS so turret is at front of ship not to hit the mast.
            Vector3 rot = cs.transform.rotation.eulerAngles;
            rot = new Vector3(rot.x, rot.y + 180, rot.z);
            Vector3 pos = new Vector3(cs.transform.position.x, cs.transform.position.y, cs.transform.position.z);
            //Spawn MLRS
            MLRS replacement = GameManager.server.CreateEntity("assets/content/vehicles/mlrs/mlrs.entity.prefab", pos, cs.transform.rotation) as MLRS;
            if (replacement == null) { return; }
            replacement.Spawn();
            replacement.SetParent(cs);
            mlrs.Add(replacement);
            replacement.transform.position = pos;
            replacement.transform.localPosition += config.CargoOffset;
            replacement.transform.rotation = Quaternion.Euler(rot);
            replacement.enabled = true;
            replacement.EnableSaving(false);
            //Add component to over-ride the shoot button and few functions
            replacement.gameObject.AddComponent<MLRSShoot>();
            if (config.Ready || config.RemoteMode)
            {
                //Try catch since some times errors OnServerInitialized if server crashed,
                try
                {
                    active = false;
                    thrower = null;
                    replacement.AdminFixUp();
                }
                catch { }
            }
            replacement.SendNetworkUpdateImmediate();
            if (config.PublicAnouncements)
            {
                CreateAnouncment("Status: <color=green>Active</color>");
            }
            if (showDebug) Puts("Spawned new Cago MLRS");
            ////Change NPCs health to make them a little harder for MLRS event
            if (config.healthmulti != 1f)
            {
                //Delay to allow NPCs to spawn
                timer.Once(2f, () =>
                {
                    if (replacement != null)
                    {
                        BoostNPCs(replacement);
                    }
                });
            }
        }

        private void GiveMLRSSignal(BasePlayer player)
        {
            if (showDebug) Puts("Remote Token Given to " + player.displayName);
            //Give skinned smoke grenade item to player
            var item = CreateItem();
            if (item != null && player != null)
            {
                player.GiveItem(item);
                if (config.PublicAnouncements)
                {
                    CreateAnouncment("MLRS Remote signal claimed by <color=red>" + player.displayName + "</color>");
                    //PrintToChat("MLRS Remote signal claimed by " + player.displayName);
                }
            }
        }

        private Item CreateItem()
        {
            //create item
            var item = ItemManager.CreateByName("grenade.smoke", 1, 2647098356);
            if (item != null)
            {
                item.text = "MLRS Strike";
                item.name = item.text;
            }
            return item;
        }

        //Create boombox below grenade for some sfx
        private void CreateSound(BaseEntity player, string url)
        {
            //Checks provided URL
            if (url == "") return;
            //Creates Resizeable Sphere
            SphereEntity sph = (SphereEntity)GameManager.server.CreateEntity("assets/prefabs/visualization/sphere.prefab", default(Vector3), default(Quaternion), true);
            DestroyMeshCollider(sph);
            sph.Spawn();
            //Create boombox
            DeployableBoomBox boombox = GameManager.server.CreateEntity("assets/prefabs/voiceaudio/boombox/boombox.deployed.prefab", default(Vector3), default(Quaternion), true) as DeployableBoomBox;
            DestroyMeshCollider(boombox);
            //Asigns boombox to sphere
            boombox.SetParent(sph);
            boombox.Spawn();
            //Setup boombox
            boombox.pickup.enabled = false;
            boombox.BoxController.ServerTogglePlay(false);
            boombox.BoxController.AssignedRadioBy = player.OwnerID;
            //Resize shpere
            sph.LerpRadiusTo(0.01f, 1f);
            //Delay to allow clean resize
            timer.Once(1f, () =>
            {
                if (sph != null)
                    sph.SetParent(player);
                sph.transform.localPosition = new Vector3(0, -1.5f, 0f);
                //Change boombox channel to url
                boombox.BoxController.CurrentRadioIp = url;
                boombox.BoxController.baseEntity.ClientRPC<string>(null, "OnRadioIPChanged", boombox.BoxController.CurrentRadioIp);
                boombox.BoxController.ServerTogglePlay(true);
                //Suiside timer for sphere and boombox
                timer.Once(40f, () =>
                {
                    try
                    {
                        if (sph != null)
                            sph?.Kill();
                    }
                    catch { }
                });
            });
            sph.SendNetworkUpdateImmediate();
        }

        //Play visual effects on entity
        private static void RunEffect(string name, BaseEntity entity = null, Vector3 position = new Vector3(), Vector3 offset = new Vector3())
        {
            if (entity != null)
                Effect.server.Run(name, entity, 0, offset, position, null, true);
            else Effect.server.Run(name, position, Vector3.up, null, true);
        }

        //Gets grid letter from world position
        string getGrid(Vector3 pos)
        {
            //Set base letter
            char letter = 'A';
            var x = Mathf.Floor((pos.x + (ConVar.Server.worldsize / 2)) / 146.3f) % 26;
            var z = (Mathf.Floor(ConVar.Server.worldsize / 146.3f)) - Mathf.Floor((pos.z + (ConVar.Server.worldsize / 2)) / 146.3f);
            letter = (char)(((int)letter) + x);
            //-1 since starts at 0
            return $"{letter}{z - 1}";
        }

        //Destory collider so player can pass though
        void DestroyMeshCollider(BaseEntity ent)
        {
            foreach (var mesh in ent.GetComponentsInChildren<MeshCollider>())
            {
                UnityEngine.Object.DestroyImmediate(mesh);
            }
        }

        //Sends message to all active players under a steamID
        void CreateAnouncment(string msg)
        {
            foreach (BasePlayer current in BasePlayer.activePlayerList.ToArray())
            {
                if (current.IsConnected)
                {
                    rust.SendChatMessage(current, "<color=orange>Cargo MLRS</color>", msg, config.AnnouncementIcon);
                }
            }
        }

        //FindNPCs and boost them
        private void BoostNPCs(MLRS pos)
        {
            int boosted = 0;
            //Scan area
            RaycastHit[] hits = UnityEngine.Physics.SphereCastAll(pos.transform.position, config.NPCRadius, Vector3.one);
            foreach (RaycastHit hit in hits.ToArray())
            {
                //Check each hit is NPC
                NPCPlayer bnpc = hit.GetEntity()?.GetComponent<NPCPlayer>();
                if (bnpc != null)
                {
                    //Checks if kit string is valid
                    if (config.Kitname != "")
                    {
                        //Calls kits plugin
                        object success = Kits?.Call("GiveKit", bnpc, config.Kitname);
                        //Trys to equip stuff
                        Item projectileItem = null;
                        foreach (var item in bnpc.inventory.containerBelt.itemList)
                        {
                            if (item.GetHeldEntity() is BaseProjectile)
                            {
                                projectileItem = item;
                                break;
                            }
                        }
                        if (projectileItem != null)
                        {
                            bnpc.UpdateActiveItem(projectileItem.uid);
                        }
                        else bnpc.EquipWeapon();
                    }
                    //Give a better name
                    bnpc.displayName = RandomUsernames.Get(bnpc.userID);
                    //Mod health
                    bnpc.startHealth *= config.healthmulti;
                    bnpc.InitializeHealth(100 * config.healthmulti, 100 * config.healthmulti);
                    boosted++;
                }
            }
            if (showDebug) Puts("Boosted " + boosted.ToString() + " NPCs");
        }

        //Functions for Cargo MLRS
        public class MLRSShoot : FacepunchBehaviour
        {
            public bool Token = true;
            public MLRS me;
            public BasePlayer seatedplayer;
            private void Awake()
            {
                //Gives reference
                me = GetComponent<MLRS>();
                //Check if should be monitoring Seat
                if (!_plugin.config.RemoteMode)
                {
                    //Setup update loop
                    InvokeRepeating(update, UnityEngine.Random.Range(0f, 1f), 0.15f);
                }
            }

            //Manually set the target
            public void SetTarget(Vector3 nt)
            {
                me.trueTargetHitPos = nt;
                me.SetUserTargetHitPos(nt);
                me.SetUserTargetHitPos(nt);
            }

            //Manually fire rockets
            public void ForceFire()
            {
                //_FireNextRocket.Invoke(me, null);
                me.FireNextRocket();
            }

            public void ForceFireRepeat(BasePlayer bp)
            {
                //Lock taget pos
                me.SetFlag(BaseEntity.Flags.Reserved6, true, false, true);
                me.nextRocketIndex = me.RocketAmmoCount - 1;
                //Fire rockets
                InvokeRepeating(ForceFire, 0f, 0.5f);
            }

            private void update()
            {
                //Try catch to prevent red text on admin cargo remove.
                try
                {
                    //Checks for mounted player
                    if (me.GetMounted() == null)
                    {
                        seatedplayer = null;
                        return;
                    }
                    //Check mounted player has changed to stop help text spamming
                    if (seatedplayer != me.GetMounted())
                    {
                        seatedplayer = me.GetMounted();
                        seatedplayer.ChatMessage("Hold Left click to <color=red>FIRE!</color>");
                    }
                    //Triggers manually shooting with left mouse button
                    if (seatedplayer != null && seatedplayer.serverInput.IsDown(BUTTON.FIRE_PRIMARY))
                    {
                        //Triggers normal fire function of press button
                        me.IsRealigning = false;
                        me.Fire(seatedplayer);
                    }
                }
                catch { }
            }
        }

        //Debug comand
        [ChatCommand("mlrscargotest")]
        private void Cmdmlrscargotest(BasePlayer player, string command, string[] args)
        {
            //Gives admin token without having to visit cargoship
            if (player.IPlayer.HasPermission(AdminPerm))
            {
                GiveMLRSSignal(player);
            }
            else
            {
                player.ChatMessage("No Perm");
            }
        }
        //Reset event
        [ChatCommand("mlrscargoreset")]
        private void Cmdmlrscargoreset(BasePlayer player, string command, string[] args)
        {
            //Respawns MLRS
            if (player.IPlayer.HasPermission(AdminPerm))
            {
                ResetCargoMLRS();
            }
            else
            {
                player.ChatMessage("No Perm");
            }
        }
    }
}