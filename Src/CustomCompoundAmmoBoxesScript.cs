using BepInEx;
using HarmonyLib;
using UnityEngine;
using Receiver2;
using Receiver2ModdingKit;
using System.IO;
using RewiredConsts;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine.Events;
using TMPro;
using System;

namespace CustomCompoundAmmoBoxes
{
    //that's interesting, if you reorder the letters in CCAB, you get ACAB. huh.
    [BepInPlugin("Ciarencew.CCAB", PluginInfo.PLUGIN_NAME, "1.1.0")]
    public class CustomCompoundAmmoBoxesScript : BaseUnityPlugin
    {
        //there might be a few things that aren't needed, maybe some fields to cleanup, etc...
        //for now, it works fine.
        private ReceiverCoreScript RCS;
        private List<GameObject> custom_ammo_boxes = new List<GameObject>();
        private GameObject fallback_ammo_box;
        private GameObject challenge_dome_ammo_box;
        private GameObject weapon_room_ammo_box;
        private GameObject shooting_range_ammo_box;
        private bool found_custom_ammo_box;
        private List<GameObject> tape_strips = new List<GameObject>();
        private GameObject tape_strips_text;
        private List<GameObject> vanilla_rounds = new List<GameObject>();
        private Vector3 challenge_dome_ammo_box_pos = new Vector3(-0.39f, 0.9f, 0.23f);
        private Vector3 weapon_room_ammo_box_pos = new Vector3(3.08f, 0.99f, 20.1f);
        private Vector3 shooting_range_ammo_box_pos = new Vector3(-1.57f, 0.59f, 0.05f);
        private GameObject current_ammo_box;
        private List<GameObject> current_tape_strips = new List<GameObject>();
        private TextMeshPro current_tape_strip_text;

        private AssetBundle[] Asset_Bundles
        {
            get
            {
                //I FUCKING LOVE REFLECTIONSSSSSSSSS
                /*var assetBundlesRCS = ((List<AssetBundle>)AccessTools.Field(typeof(ReceiverCoreScript), "asset_bundles").GetValue(ReceiverCoreScript.Instance()));
                if (assetBundlesRCS.Count > 0)
                {
                    return assetBundlesRCS.ToArray();
                }*/

                //this whole thing up there is useless because sometimes (????) the RCS's asset_bundles field has stuff in it? but idk why?
                //and when I tried it the FN57 didn't appear but all other guns did (??????????)
                //so whatever fuck it let's sift through a million asset bundles (actually like 20 or so)
                return AssetBundle.GetAllLoadedAssetBundles_Native(); //arrays are faster to look through.
            }
        }

        private void Awake()
        {
            // Plugin startup logic
            Logger.LogInfo($"Plugin {PluginInfo.PLUGIN_GUID} is loaded!");

            ReceiverEvents.StartListening(ReceiverEventTypeVoid.PlayerInitialized, new UnityAction<ReceiverEventTypeVoid>(PlayerInitialized));

        }

        private void PlayerInitialized(ReceiverEventTypeVoid ev)
        {
            RCS = ReceiverCoreScript.Instance();

            if (RCS.game_mode.GetGameMode() != GameMode.ReceiverMall)
            {
                //is doing this even useful? idk.
                ReceiverEvents.StopListening(ReceiverEventTypeVoid.PlayerPickupGun, new UnityAction<ReceiverEventTypeVoid>(PlayerPickedUpGun));
                ReceiverEvents.StopListening(ReceiverEventTypeVoid.PlayerDropGun, new UnityAction<ReceiverEventTypeVoid>(PlayerDroppedGun));
                ReceiverEvents.StopListening(ReceiverEventTypeGameObject.PlayerInteractWithObject, new UnityAction<ReceiverEventTypeGameObject, GameObject>(PlayerDiscaredItem));
                return;
            };


            if (tape_strips == null || tape_strips_text == null)
            {
                GetTapeStrips();
            }
            if (vanilla_rounds.Count == 0) //in case of a restart
            {
                GetVanillaRounds();
            }
            if (fallback_ammo_box == null) //just in case
            {
                LoadFallbackBoxes();
                GetCustomBoxes();
            }
            else Debug.LogWarning("Already assigned custom ammo boxes, skipping...");

            if (RCS.RoundPrefab != null)
            {
                DestroyCustomBoxes();
                if (TryGetCustomAmmoBoxForCurrentRound())
                {
                    SpawnCustomBoxes();
                }
            }

            ReceiverEvents.StartListening(ReceiverEventTypeVoid.PlayerPickupGun, new UnityAction<ReceiverEventTypeVoid>(PlayerPickedUpGun));
            ReceiverEvents.StartListening(ReceiverEventTypeVoid.PlayerDropGun, new UnityAction<ReceiverEventTypeVoid>(PlayerDroppedGun)); //in case I decide to make that weird QoL mod I've been thinking of doing.
            ReceiverEvents.StartListening(ReceiverEventTypeGameObject.PlayerInteractWithObject, new UnityAction<ReceiverEventTypeGameObject, GameObject>(PlayerDiscaredItem));
        }

        private void PlayerDroppedGun(ReceiverEventTypeVoid ev)
        {
            DestroyCustomBoxes();
        }
        private void PlayerDiscaredItem(ReceiverEventTypeGameObject ev, GameObject gameobject)
        {
            if ((gameobject.TryGetComponent<Pegboard>(out _) || gameobject.TryGetComponent<GunPlacementSlot>(out _)))
            {
                if (!RCS.player.lah.TryGetGun(out _))
                {
                    Debug.Log("player discarded his gun, checking if custom box needs destroying");
                    DestroyCustomBoxes(); //if the player doesn't have a gun, destroy em.
                }
                else Debug.Log("player still has his gun, won't destroy ammo boxes.");
            }
        }

        private void PlayerPickedUpGun(ReceiverEventTypeVoid ev)
        {
            DestroyCustomBoxes();
            if (TryGetCustomAmmoBoxForCurrentRound())
            {
                SpawnCustomBoxes();
            }
        }

        private void GetVanillaRounds()
        {
            var shooting_range_table = transform.Find("/Shooting Range/Gameplay/Ammo Tables");
            foreach (Transform ammo_box in shooting_range_table)
            {
                ShootingRangeAmmoBoxScript srab; //srab is the acronym for Shooting Range Ammo Box, I know, very catchy
                if (ammo_box.TryGetComponent<ShootingRangeAmmoBoxScript>(out srab))
                {
                    vanilla_rounds.Add(srab.round_prefab);
                }
            }
        }

        private void GetTapeStrips()
        {
            if (tape_strips != null) tape_strips.Clear();
            tape_strips_text = null;
            var tape_strips_parent = transform.Find("/Shooting Range/Gameplay/Ammo Tables/AmmoBox45LC (2)/Tape .45 LC");
            foreach (Transform tape_strip in tape_strips_parent)
            {
                if (tape_strip.gameObject.TryGetComponent<TMPro.TextMeshPro>(out _))
                {
                    tape_strips_text = tape_strip.gameObject;
                }
                else
                {
                    tape_strips.Add(tape_strip.gameObject);
                }
            }
        }
        private void SetCustomTapeStrips(GameObject ammo_box, string round_name)
        {
            var tape_strips_fallback = new GameObject(string.Format("Tape {0}", current_ammo_box.GetComponent<ShootingRangeAmmoBoxScript>().round_prefab.name));
            tape_strips_fallback.transform.parent = ammo_box.transform;
            tape_strips_fallback.transform.localPosition = new Vector3(0.125f, 0.012f, 0f);
            foreach (GameObject tape_strip in tape_strips)
            {
                var new_tape_strip = UnityEngine.Object.Instantiate(tape_strip, tape_strips_fallback.transform);
                new_tape_strip.SetActive(false);
                current_tape_strips.Add(new_tape_strip);
            }
            current_tape_strip_text = UnityEngine.Object.Instantiate(tape_strips_text, tape_strips_fallback.transform).GetComponent<TextMeshPro>();
            current_tape_strip_text.text = round_name;
            current_tape_strip_text.GetTextInfo(current_tape_strip_text.text); //even though it does it on its own when you instantiate an object (I think), you still have to do it manually, so dumb.
            if (current_tape_strips.Count != 0)
            {
                int i;
                for (i = 0; i < current_tape_strips.Count - 1; i++)
                {
                    var tape_strip_text_x = current_tape_strip_text.textBounds.extents.x;
                    var tape_strip_z = current_tape_strips[i].GetComponent<MeshFilter>().mesh.bounds.extents.z;
                    if (tape_strip_z < tape_strip_text_x)
                    {
                        Debug.LogFormat("{0} > {1}", tape_strip_z, tape_strip_text_x);
                        break;
                    }
                    Debug.LogFormat("{0} < {1}", tape_strip_z, tape_strip_text_x);
                    //Debug.Log(string.Format("{0} > {1}", tape_strip_text_x, tape_strip_x));
                }
                current_tape_strips[Mathf.Clamp(i - 1, 0, current_tape_strips.Count - 1)].SetActive(true);
            }
            Debug.Log("Custom tape strips set!");
        }

        private void DestroyCustomBoxes()
        {
            if (current_tape_strips.Count != 0)
            {
                var parent = current_tape_strip_text.gameObject.transform.parent;
                current_tape_strips.Clear();
                current_tape_strip_text = null;
                Destroy(parent.gameObject);
            }
            if (challenge_dome_ammo_box != null) Destroy(challenge_dome_ammo_box);
            if (weapon_room_ammo_box != null) Destroy(weapon_room_ammo_box);
            if (shooting_range_ammo_box != null) Destroy(shooting_range_ammo_box);
        }

        private void SpawnCustomBoxes()
        {
            challenge_dome_ammo_box = UnityEngine.Object.Instantiate<GameObject>(current_ammo_box, Vector3.zero, Quaternion.identity, transform.Find("/Challenge Room/Challenge Room Geometry/AmmoTable"));
            weapon_room_ammo_box = UnityEngine.Object.Instantiate<GameObject>(current_ammo_box, Vector3.zero, Quaternion.identity, transform.Find("/Weapon Storage Room/CollectionRoomTestAssets/WorkbenchProps"));
            shooting_range_ammo_box = UnityEngine.Object.Instantiate<GameObject>(current_ammo_box, Vector3.zero, Quaternion.identity, transform.Find("/Shooting Range/Gameplay/Ammo Tables/"));

            //how fucking stupid is it that the position that you give it when also giving it a parent is still the global one?
            challenge_dome_ammo_box.transform.localPosition = challenge_dome_ammo_box_pos;

            weapon_room_ammo_box.transform.localPosition = weapon_room_ammo_box_pos;
            var wep_ammo_box_rot = weapon_room_ammo_box.transform.localRotation;
            wep_ammo_box_rot.y = 0.7071f;
            wep_ammo_box_rot.w = 0.7071f;
            weapon_room_ammo_box.transform.localRotation = wep_ammo_box_rot;

            shooting_range_ammo_box.transform.localPosition = shooting_range_ammo_box_pos;
            var shoot_range_ammo_box_rot = shooting_range_ammo_box.transform.localRotation;
            shoot_range_ammo_box_rot.y = 0.7071f;
            shoot_range_ammo_box_rot.w = 0.7071f;
            shooting_range_ammo_box.transform.localRotation = shoot_range_ammo_box_rot;

            Debug.Log("Instantiated custom ammo boxes :D");
        }

        private void LoadFallbackBoxes()
        {
            string current_directory = Path.GetDirectoryName(new Uri(Assembly.GetExecutingAssembly().CodeBase).LocalPath);
            Debug.Log(current_directory);
            if (Directory.Exists(current_directory)) //don't need to check but whatevs
            {
                var fallback_ammo_box_strings = Directory.GetFiles(current_directory);
                for (int i = 0; i < fallback_ammo_box_strings.Length; i++)
                {
                    string fileName = fallback_ammo_box_strings[i];
                    if (fileName.Contains(SystemInfo.operatingSystemFamily.ToString().ToLower()))
                    {
                        AssetBundle assetBundle = AssetBundle.GetAllLoadedAssetBundles().FirstOrDefault((AssetBundle bundle) => bundle.name.Contains(Path.GetFileNameWithoutExtension(fileName)));
                        if (assetBundle == null)
                        {
                            assetBundle = AssetBundle.LoadFromFile(fileName);
                            if (assetBundle == null)
                            {
                                Debug.LogWarning("Failed to load AssetBundle " + fileName);
                                return;
                            }
                        }
                        string text = Path.GetFileNameWithoutExtension(fileName);
                        foreach (string asset_name in assetBundle.GetAllAssetNames())
                        {
                            GameObject gameObject = assetBundle.LoadAsset<GameObject>(asset_name);
                            if (gameObject != null)
                            {
                                if (gameObject.TryGetComponent<ShootingRangeAmmoBoxScript>(out ShootingRangeAmmoBoxScript ammo_box_script))
                                {
                                    fallback_ammo_box = gameObject;
                                    Debug.Log("Found the fallback_ammo_box and assigned it");
                                }
                                else Debug.LogWarning("found something that wasn't a ShootingRangeAmmoBoxScript somehow");
                            }
                            else Debug.LogWarning("found something that wasn't a GameObject somehow");
                        }
                    }
                }
                if (fallback_ammo_box == null)
                {
                    Debug.LogError("fallback_ammo_box is null!!!! SHITTTTTT!!!!!!!!!!!");
                }
            }
            else Debug.LogError("Fallback boxes directory is non-existent, time to shit and cum!");
        }

        private void GetCustomBoxes()
        {
            for (int i = 0; i < Asset_Bundles.Length; i++) //for loops on arrays are about 5 times faster than foreach loops on lists
            {
                LoadCustomAmmoBoxes(Asset_Bundles[i]); 
            }
            if (custom_ammo_boxes == null)
            {
                Debug.LogWarning("No custom ammo box for the compound found");
            }
            else Debug.LogFormat("found {0} custom ammo boxes, yay!", custom_ammo_boxes.Count); //why is there a method to do format and log at once? there are so many other things that they could've tried to make into one method that they didn't, wtf
        }

        private void LoadCustomAmmoBoxes(AssetBundle assetBundle)
        {
            if (assetBundle.name.StartsWith("fallback_ammo_box")) return;
            var assetNames = assetBundle.GetAllAssetNames();
            for (int i = 0; i < assetNames.Length; i++)
            {
                GameObject gameObject = assetBundle.LoadAsset<GameObject>(assetNames[i]);
                if (gameObject != null)
                {
                    if (gameObject.TryGetComponent<ShootingRangeAmmoBoxScript>(out ShootingRangeAmmoBoxScript ammo_box_script))
                    {
                        custom_ammo_boxes.Add(gameObject);
                        Debug.Log(string.Format("Found the custom ammo box for {0} and added it to the list", ammo_box_script.round_prefab.GetComponent<ShellCasingScript>().InternalName));
                    }
                }
            }
        }

        private bool DebugCompareRounds(int num) //never actually used it, there might be a might that could be debugged using this, leaving it in.
        {
            var round_internal_name_ammo_box = custom_ammo_boxes[num].GetComponent<ShootingRangeAmmoBoxScript>().round_prefab.GetComponent<ShellCasingScript>().InternalName;
            var round_internal_name_current = RCS.RoundPrefab.GetComponent<ShellCasingScript>().InternalName;
            Debug.Log(round_internal_name_ammo_box);
            Debug.Log(round_internal_name_current);
            bool is_equal;
            Debug.Log(is_equal = (round_internal_name_current == round_internal_name_ammo_box));
            return is_equal;
        }

        private bool TryGetCustomAmmoBoxForCurrentRound()
        {
            if (current_tape_strips.Count != 0)
            {
                var parent = current_tape_strip_text.gameObject.transform.parent;
                current_tape_strips.Clear();
                current_tape_strip_text = null;
                Destroy(parent.gameObject);
            }
            found_custom_ammo_box = false;
            foreach (GameObject ammo_box in custom_ammo_boxes)
            {
                if (ammo_box.GetComponent<ShootingRangeAmmoBoxScript>().round_prefab.GetComponent<ShellCasingScript>().InternalName == RCS.RoundPrefab.GetComponent<ShellCasingScript>().InternalName)
                {
                    found_custom_ammo_box = true;
                    Debug.LogFormat("Found an custom ammo box matching the current cartridge ({0})", RCS.RoundPrefab.name);
                    current_ammo_box = ammo_box;
                    SetCustomTapeStrips(current_ammo_box, RCS.RoundPrefab.name);
                    return true;
                }
            }
            if (!found_custom_ammo_box) //has a custom ammo box for the currect cartridge been found?
            {
                if (!vanilla_rounds.Contains(RCS.RoundPrefab))
                {
                    string round_name;
                    if (!RCS.RoundPrefab.GetComponent<ShellCasingScript>().InternalName.StartsWith("wolfire."))
                    {
                        Debug.LogWarning("No custom ammo boxes matching current modded cartridge were found, falling back to default");
                        round_name = RCS.RoundPrefab.name;
                    }
                    else
                    {
                        Debug.LogWarning("This vanilla round does not have an ammo box for it. Assigning fallback...");
                        switch (RCS.RoundPrefab.GetComponent<ShellCasingScript>().cartridge_type)
                        {
                            case CartridgeSpec.Preset._556_NATO:
                                round_name = "5.56x45mm";
                                break;
                            case CartridgeSpec.Preset._50_BMG:
                                round_name = ".50 BMG";
                                break;
                            case CartridgeSpec.Preset._22_LR:
                                round_name = ".22 LR";
                                break;
                            case CartridgeSpec.Preset._762_soviet:
                                round_name = "7.62 Soviet";
                                break;
                            case CartridgeSpec.Preset._40_SW:
                                round_name = ".40 S&W";
                                break;
                            default:
                                Debug.LogErrorFormat("This vanilla round doesn't have a case for it, maybe ask the developer of this mod");
                                round_name = "uh, this shouldn't happen. :(";
                                break;
                        }
                    }
                    current_ammo_box = fallback_ammo_box;
                    current_ammo_box.GetComponent<ShootingRangeAmmoBoxScript>().round_prefab = RCS.RoundPrefab;
                    SetCustomTapeStrips(fallback_ammo_box, round_name);
                    return true;
                }
            }
            Debug.Log("Current round isn't custom");
            return false;
        }

    }
}
