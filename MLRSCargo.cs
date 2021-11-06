using Facepunch;
using Oxide.Core.Plugins;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
namespace Oxide.Plugins
{
    [Info("MLRSCargo", "bmgjet", "1.0.3")]
    [Description("Places a MLRS on the front of CargoShip, Has a event to remotely trigger MLRS")]
    public class MLRSCargo : RustPlugin
    {
        //Remote Trigger Only
        public static bool RemoteMode = true; //Must have spawnready enabled also since cant access cockpit

        //Spawns it already loaded with rockets and aiming module
        bool SpawnReady = true;
        //Delay betwen Refill
        int Delay = 600; //seconds (10sec is minimum, 600 default = 10mins)

        //Show announcements in public chat
        public bool PublicAnouncements = true;
        //Steam ID to use profile pic as announcment chat icon
        public string AnnouncementIcon = "76561199219302299";

        //Cargo NPCs health multiplyer
        float healthmulti = 3f;
        //Radius to cast NPC Scan
        float NPCRadius = 50f; //Scan first 1/4 of ship around MLRS
        //Replace Kit
        string Kitname = "";  //("" = No kit change)

        //Play SFX over boombox from remote MLRS grenade
        bool PlaySFX = true;
        //URL to SFX (Must be a raw mp3 file)
        string SFXURL = "https://github.com/bmgjet/RocketFail/blob/main/AirRaid.mp3?raw=true";

        //Position on cargoship moved from dead center
        Vector3 CargoOffset = new Vector3(0f, 9.5f, 76f);

        //Show Debug Info
        bool showDebug = false;

        //Reference Kits plugin so kits can be applied to NPCs
        [PluginReference]
        private Plugin Kits;

        //Remote control only disable mounting
        object CanMountEntity(BasePlayer bp, BaseMountable bm)
        {
            if (RemoteMode && SpawnReady && bm.ShortPrefabName == "mlrs.entity")
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
                        rust.SendChatMessage(bp, "<color=orange>Cargo MLRS</color>", "MLRS Remote signal has already been claimed", AnnouncementIcon);
                        //Disable Mount
                        return true;
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
                timer.Once(5f, () =>
                {
                    AddMLRS(cs);
                });
            }
        }

        // public FieldInfo _mapMarkerPrefab = typeof(MLRS).GetField("mapMarkerPrefab", BindingFlags.NonPublic | BindingFlags.Instance);
        void OnEntitySpawned(MLRSRocket rocket)
        {
            // Puts(_mapMarkerPrefab.GetValue(rocket).);
            //Checks if its one of the events rockets
            if (rocket != null && active)
            {
                if (rocket.OwnerID == 0)
                {
                    //Sets rocket as last user to trigger the event
                    rocket.OwnerID = thrower.userID;
                    rocket.creatorEntity = thrower;
                    global::BaseEntity baseEntity2 = GameManager.server.CreateEntity("assets/content/vehicles/mlrs/mlrsrocketmarker.prefab", rocket.transform.position, Quaternion.identity, true);
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
                if (SpawnReady)
                    NextTick(player.EndLooting);
            }
            catch { }
        }

        void OnServerInitialized()
        {
            //Validate Settings
            if (Delay < 10)
            {
                Puts("Invalid Delay Must be 10 or greater!");
                Delay = 10;
            }
            if (RemoteMode && !SpawnReady)
            {
                Puts("Spawn Ready Must Be Used With Remote Mode Since CockPit Is Unavaliable!");
                SpawnReady = true;
            }
            //try catch since throws errors if server had crashed instead of clean shutdown while cargo is out.
            try
            {
                ResetCargoMLRS();
            }
            catch { }
        }

        void Unload()
        {
            //Remove component
            var objects = GameObject.FindObjectsOfType(typeof(MLRSShoot));
            if (objects != null)
            {
                foreach (var gameObj in objects)
                {
                    GameObject.Destroy(gameObj);
                }
            }
        }

        //Active flag to limit to 1 user at a time.
        public static bool active = false;
        //Last player to trigger event
        BasePlayer thrower;
        //Gets thrown or dropped smoke grenade as trigger for remote target.
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

        void RemoteEvent(BasePlayer bp, BaseEntity be)
        {
            //Check if its an event token
            if (be.skinID != 2647098356) return;
            if (showDebug) Puts("Remote Cargo MLRS trigged by " + bp.displayName);

            RunEffect("assets/bundled/prefabs/fx/smoke_signal_full.prefab", be);
            //Checks if already active event
            if (be != null && !active)
            {
                bool Sucess = false;
                thrower = bp;
                active = true;
                if (PlaySFX)
                {
                    CreateSound(be, SFXURL);
                }
                //delay to allow smoke grenade to travel
                timer.Once(8f, () =>
                {
                    if (be == null)
                    {
                        active = false;
                        rust.SendChatMessage(bp, "<color=orange>Cargo MLRS</color>", "Your MLRS remote signal <color=red>FAILED</color>", AnnouncementIcon);
                        return;
                    }
                    Vector3 pos = be.transform.position;
                    if (pos != null)
                    {
                        if (PublicAnouncements)
                        {
                            CreateAnouncment("<color=red>" + bp.displayName + "</color> has called a MLRS strike near <color=orange>" + getGrid(pos) + "</color>");
                            //PrintToChat("<color=orange>"+bp.displayName + "</color> has called a MLRS strike near <color=red>" + getGrid(pos)+"</color>");
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
                                                rust.SendChatMessage(bp, "<color=orange>Cargo MLRS</color>", "Cargo MLRS </color=red>Not Avaliable</color=red>", AnnouncementIcon);
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
                                            if (PublicAnouncements)
                                            {
                                                CreateAnouncment("Status: <color=red>Disabled</color>");
                                            }
                                        });
                                        //Resets the cargo after delay
                                        if (SpawnReady)
                                        {
                                            timer.Once(10 + Delay, () =>
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
                rust.SendChatMessage(bp, "<color=orange>Cargo MLRS</color>", "Cargo MLRS Not Avaliable", AnnouncementIcon);
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
                        foreach (BaseEntity b in bef.ToArray())
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
            if (showDebug) Puts("Cleaned " + cleaned.ToString() + " dupes");
        }

        void AddMLRS(CargoShip cs)
        {
            CleanDupedMLRS(cs.transform.position + CargoOffset);
            //Rotate MLRS so turret is at front of ship not to hit the mast.
            Vector3 rot = cs.transform.rotation.eulerAngles;
            rot = new Vector3(rot.x, rot.y + 180, rot.z);
            Vector3 pos = new Vector3(cs.transform.position.x, cs.transform.position.y, cs.transform.position.z);
            //Spawn MLRS
            MLRS replacement = GameManager.server.CreateEntity("assets/content/vehicles/mlrs/mlrs.entity.prefab", pos, cs.transform.rotation) as MLRS;
            if (replacement == null) return;
            replacement.Spawn();
            replacement.SetParent(cs);
            replacement.transform.position = pos;
            replacement.transform.localPosition += CargoOffset;
            replacement.transform.rotation = Quaternion.Euler(rot);
            replacement.enabled = true;
            //Add component to over-ride the shoot button and few functions
            replacement.gameObject.AddComponent<MLRSShoot>();
            if (SpawnReady || RemoteMode)
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
            if (PublicAnouncements)
            {
                CreateAnouncment("Status: <color=green>Active</color>");
            }
            if (showDebug) Puts("Spawned new Cago MLRS");
            ////Change NPCs health to make them a little harder for MLRS event
            if (healthmulti != 1f)
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
                if (PublicAnouncements)
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
            char letter = 'A';
            var x = Mathf.Floor((pos.x + (ConVar.Server.worldsize / 2)) / 146.3f) % 26;
            var z = (Mathf.Floor(ConVar.Server.worldsize / 146.3f)) - Mathf.Floor((pos.z + (ConVar.Server.worldsize / 2)) / 146.3f);
            letter = (char)(((int)letter) + x);
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
                    rust.SendChatMessage(current, "<color=orange>Cargo MLRS</color>", msg, AnnouncementIcon);
                }
            }

        }

        private bool IsKit(string kit)
        {
            //Call kit plugin check if its valid kit
            var success = Kits?.Call("isKit", kit);
            if (success == null || !(success is bool))
            {
                return false;
            }
            return (bool)success;
        }

        //FindNPCs and boost them
        private void BoostNPCs(MLRS pos)
        {
            int boosted = 0;
            //Scan area
            RaycastHit[] hits = UnityEngine.Physics.SphereCastAll(pos.transform.position, NPCRadius, Vector3.one);
            foreach (RaycastHit hit in hits.ToArray())
            {
                //Check each hit is NPC
                NPCPlayer bnpc = hit.GetEntity()?.GetComponent<NPCPlayer>();
                if (bnpc != null)
                {
                    //Checks if kit string is valid
                    if (Kitname != "" && IsKit(Kitname))
                    {
                        //Calls kits plugin
                        object success = Kits?.Call("GiveKit", bnpc, Kitname);
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
                    bnpc.startHealth *= healthmulti;
                    bnpc.InitializeHealth(100 * healthmulti, 100 * healthmulti);
                    boosted++;
                }
            }
            if (showDebug) Puts("Boosted " + boosted.ToString() + " NPCs");
        }

        //Functions for Cargo MLRS
        public class MLRSShoot : FacepunchBehaviour
        {
            //Reflection to pull the functions from private MLRS class
            public MethodInfo _fire = typeof(MLRS).GetMethod("Fire", BindingFlags.NonPublic | BindingFlags.Instance);
            public MethodInfo _SetUserTargetHitPos = typeof(MLRS).GetMethod("SetUserTargetHitPos", BindingFlags.NonPublic | BindingFlags.Instance);
            public MethodInfo _FireNextRocket = typeof(MLRS).GetMethod("FireNextRocket", BindingFlags.NonPublic | BindingFlags.Instance);
            public PropertyInfo _IsRealigning = typeof(MLRS).GetProperty("IsRealigning");
            public PropertyInfo _TrueHitPos = typeof(MLRS).GetProperty("TrueHitPos");
            public PropertyInfo _UserTargetHitPos = typeof(MLRS).GetProperty("UserTargetHitPos");
            public FieldInfo _nextRocketIndex = typeof(MLRS).GetField("nextRocketIndex", BindingFlags.NonPublic | BindingFlags.Instance);
            public bool Token = true;
            public MLRS me;
            public BasePlayer seatedplayer;
            private void Awake()
            {
                //Gives reference
                me = GetComponent<MLRS>();
                //Check if should be monitoring Seat
                if (!RemoteMode)
                {
                    //Setup update loop
                    InvokeRepeating(update, UnityEngine.Random.Range(0f, 1f), 0.15f);
                }
            }

            //Manually set the target
            public void SetTarget(Vector3 nt)
            {
                _TrueHitPos.SetValue(me, nt);
                _UserTargetHitPos.SetValue(me, nt);
                _SetUserTargetHitPos.Invoke(me, new object[] { nt });
            }

            //Manually fire rockets
            public void ForceFire()
            {
                _FireNextRocket.Invoke(me, null);
            }

            public void ForceFireRepeat(BasePlayer bp)
            {
                //Lock taget pos
                me.SetFlag(global::BaseEntity.Flags.Reserved6, true, false, true);
                //Sets next rocket to fire
                _nextRocketIndex.SetValue(me, me.RocketAmmoCount - 1);
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
                        _IsRealigning.SetValue(me, false);
                        _fire.Invoke(me, new object[] { seatedplayer });
                    }
                }
                catch { }
            }
        }

        //Debug comand
        [ChatCommand("mlrscargotest")]
        private void CmdSafeSpaceCraft(BasePlayer player, string command, string[] args)
        {
            //Gives admin token without having to visit cargoship
            if (player.IsAdmin)
                GiveMLRSSignal(player);
        }
    }
}