using BepInEx;
using BepInEx.Logging;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using HarmonyLib;
using System.IO;
using System.Reflection;
using Photon.Pun;
using UnityEngine.EventSystems;
using System.Collections.Generic;
using System.Collections;
using BepInEx.Configuration;
using UnityEngine.SceneManagement;
using Zorro.Settings;
using UnityEngine.Localization;
using System.Text.RegularExpressions;
using Zorro.Settings.UI;

namespace Hunter;

// Here are some basic resources on code style and naming conventions to help
// you in your first CSharp plugin!
// https://learn.microsoft.com/en-us/dotnet/csharp/fundamentals/coding-style/coding-conventions
// https://learn.microsoft.com/en-us/dotnet/csharp/fundamentals/coding-style/identifier-names
// https://learn.microsoft.com/en-us/dotnet/standard/design-guidelines/names-of-namespaces

// This BepInAutoPlugin attribute comes from the Hamunii.BepInEx.AutoPlugin
// NuGet package, and it will generate the BepInPlugin attribute for you!
// For more info, see https://github.com/Hamunii/BepInEx.AutoPlugin
[BepInAutoPlugin]
public partial class Plugin : BaseUnityPlugin
{
    internal static ManualLogSource Log { get; private set; } = null!;

    public static Plugin _;
    //Player Data
    private static Dictionary<int, bool> playersReady = new Dictionary<int, bool>();
    private static int randomBlowgunRunner = -1;

    //Hunter Data
    private static List<int> hunterDatabase = new List<int>();
    private static float hunterCooldown = -1000;
    ConfigFile hunterConfigData = new ConfigFile(Path.Combine(Paths.ConfigPath, "BT_Hunter.cfg"), true);
    // Gamemode
    ConfigEntry<bool> zombieMode;
    ConfigEntry<bool> pickRandom;
    ConfigEntry<int> initialCooldown;
    ConfigEntry<int> additionalCooldown;
    ConfigEntry<bool> disableScoutmaster;
    // Climber
    ConfigEntry<float> climberExtraStamina;
    ConfigEntry<float> climberDamageMultiplier;
    ConfigEntry<bool> startWithBlowgun;
    ConfigEntry<float> blowgunCooldown;
    // Hunter
    ConfigEntry<float> hunterExtraStamina;
    ConfigEntry<float> hunterDamageMultiplier;
    ConfigEntry<bool> enableHunterAttack;
    ConfigEntry<float> attackDrowsiness;
    ConfigEntry<float> attackKnockbackMultiplier;
    ConfigEntry<string> attackType;
    ConfigEntry<float> attackAmount;

    //Assets
    private static AssetBundle assets;
    private static Sprite climberSprite;
    private static Sprite hunterSprite;

    //General UI
    private static Image smallRoleIcon;
    private static GameObject hunterNearPrefab;
    //PassportUI
    private static TextMeshProUGUI roleLabel;
    private static Button roleSwitcher;
    //BoardingPassUI
    private static BoardingPass boardingPass;
    //Menu UI
    static SettingsTABSButton hunterTab;

    //Client to Server Updater
    public class HunterPlayerUpdater : MonoBehaviourPun
    {
        //Update Config Info
        [PunRPC]
        public void RPC_RecieveConfigData(string section, string key, object value)
        {
            switch (value)
            {
                case float f:
                    ConfigEntry<float> configEntryFloat = (ConfigEntry<float>)_.hunterConfigData[section, key];
                    configEntryFloat.Value = f;
                    break;
                case int i:
                    ConfigEntry<int> configEntryInt = (ConfigEntry<int>)_.hunterConfigData[section, key];
                    configEntryInt.Value = i;
                    break;
                case bool b:
                    ConfigEntry<bool> configEntryBool = (ConfigEntry<bool>)_.hunterConfigData[section, key];
                    configEntryBool.Value = b;
                    break;
                case string s:
                    ConfigEntry<string> configEntryString = (ConfigEntry<string>)_.hunterConfigData[section, key];
                    configEntryString.Value = s;
                    break;
            }
            Log.LogDebug("Set \"" + section + " - " + key + "\" to " + value);

            _.UpdateHunterModSettings();
        }

        //Update Roles on all clients
        [PunRPC]
        public void RPCA_ChangeRole(int ActorNumber, bool isHunter)
        {
            if (!isHunter && hunterDatabase.Contains(ActorNumber))
                hunterDatabase.Remove(ActorNumber);
            else if (isHunter && !hunterDatabase.Contains(ActorNumber))
                hunterDatabase.Add(ActorNumber);

            //Update BoardingPass UI for All
            if (boardingPass != null)
                BoardingPassUIPatch(boardingPass);
        }

        //Server creates Blowgun
        [PunRPC]
        public void RPC_SpawnBlowgun(int actorNumber, bool ignoreHunter)
        {
            if (_.startWithBlowgun.Value && PhotonNetwork.IsMasterClient)
            {
                StartCoroutine(delay());
                IEnumerator delay()
                {
                    yield return new WaitForSeconds(5);

                    //Get correct character
                    Character character = null;
                    foreach (Character charac in Character.AllCharacters)
                        if (charac.view.Owner.ActorNumber == actorNumber)
                            character = charac;

                    if (character != null)
                    {
                        Log.LogDebug("Server: Try give blowgun to " + character.characterName);

                        //Give Blowgun to 1 Random Climber
                        if ((!isHunter(character) && actorNumber == randomBlowgunRunner) || ignoreHunter)
                        {
                            Item component = PhotonNetwork.InstantiateItemRoom("HealingDart Variant", character.transform.position, character.transform.rotation).GetComponent<Item>();
                            //Attach special component on all Clients
                            character.view.RPC("RPCA_EquippedBlowgun", RpcTarget.All, character.GetComponent<PhotonView>(), component.GetComponent<PhotonView>());

                            Log.LogDebug("Server: Spawned Blowgun for Traveler");
                        }
                    }
                }
            }
        }

        //Attach Special Component to Reusable Blowgun
        [PunRPC]
        public void RPCA_EquippedBlowgun(PhotonView characterView, PhotonView itemView)
        {
            Item item = itemView.GetComponent<Item>();

            StartCoroutine(waitForItemLoad());
            IEnumerator waitForItemLoad()
            {
                yield return new WaitUntil(() => item.data != null);
                OptionableIntItemData invalidData = new OptionableIntItemData();
                invalidData.HasData = true;
                invalidData.Value = 1;
                item.data.RegisterEntry(DataEntryKey.INVALID, invalidData);
                Log.LogDebug("Client: Modified Blowgun Data");

                //Auto Pickup
                yield return null;
                if (PhotonNetwork.IsMasterClient)
                    item.RequestPickup(characterView);
            }
        }
    }

    private static bool isLocalHunter()
    {
        return isHunter(Character.localCharacter);
    }

    private static bool isHunter(Character character)
    {
        return hunterDatabase.Contains(character.view.Owner.ActorNumber);
    }

    private static void changeCharacterReady(Character character, bool isReady)
    {
        if (!playersReady.ContainsKey(character.view.Owner.ActorNumber))
            playersReady.Add(character.view.Owner.ActorNumber, isReady);
        else
            playersReady[character.view.Owner.ActorNumber] = isReady;
    }

    private static bool playersReadyForHunter()
    {
        bool flag = true;
        foreach (Photon.Realtime.Player player in PhotonNetwork.PlayerList)
            //Check if Character object has "awoken"
            if (!playersReady.ContainsKey(player.ActorNumber) || !playersReady[player.ActorNumber])
                flag = false;
            else
            {
                //Check if Character is still in loading screen
                Character character = PlayerHandler.GetPlayerCharacter(player);
                if (character.data.passedOutOnTheBeach > 0)
                    flag = false;
            }
        return flag;
    }

    private IEnumerator showMessage(string message)
    {
        Log.LogDebug("Show Message: " + message);
        GameObject hunterNear = Instantiate(hunterNearPrefab, hunterNearPrefab.transform.parent);
        hunterNear.transform.Find("Fog").GetComponent<TextMeshProUGUI>().text = message;
        hunterNear.SetActive(true);
        yield return new WaitForSeconds(4);
        Destroy(hunterNear);
    }

    //Loads Plugin
    private void Awake()
    {
        // BepInEx gives us a logger which we can use to log information.
        // See https://lethal.wiki/dev/fundamentals/logging
        Log = Logger;

        // BepInEx also gives us a config file for easy configuration.
        // See https://lethal.wiki/dev/intermediate/custom-configs

        // We can apply our hooks here.
        // See https://lethal.wiki/dev/fundamentals/patching-code

        _ = this;

        //Config File
        // Gamemode
        zombieMode = hunterConfigData.Bind("_Gamemode", "ZombieMode", false,
            "When Enabled, once Climbers die, they join the Hunter's Team");
        pickRandom = hunterConfigData.Bind("_Gamemode", "PickRandomHunter", false,
            "Upon Game Start, a random Hunter will be chosen");
        initialCooldown = hunterConfigData.Bind("_Gamemode", "InitialHunterCooldown", 10,
            "Change the Cooldown of how long the Hunter is knocked out");
        additionalCooldown = hunterConfigData.Bind("_Gamemode", "AddedCooldownPerSection", 3,
            "Increases the amount of Cooldown applied after each Section");
        disableScoutmaster = hunterConfigData.Bind("_Gamemode", "DisableScoutmaster", true,
            "Scoutmaster may be problematic with Hunter/Climber strategies!");
        // Climber
        climberExtraStamina = hunterConfigData.Bind("ClimberStats", "ExtraStamina", 0f,
            "Applies this extra Stamina when the Climber is rested");
        climberDamageMultiplier = hunterConfigData.Bind("ClimberStats", "FallDamageMultiplier", 0.5f,
            "Reduced/Increases the amount of Damage the Climber takes. (Not Including the Hunter Attack)");
        startWithBlowgun = hunterConfigData.Bind("ClimberStats", "StartWithBlowgun", true,
            "Determines if 1 Random Climber starts with a Blowdart");
        blowgunCooldown = hunterConfigData.Bind("ClimberStats", "BlowgunCooldownInMins", 7f,
            "When the Blowgun will be usable again");
        // Hunter
        hunterExtraStamina = hunterConfigData.Bind("HunterStats", "ExtraStamina", 0.5f,
            "Applies this extra Stamina when the Hunter is rested");
        hunterDamageMultiplier = hunterConfigData.Bind("HunterStats", "FallDamageMultiplier", 0.5f,
            "Reduced/Increases the amount of Damage the Hunter takes");
        enableHunterAttack = hunterConfigData.Bind("HunterStats", "EnableHunterAttack", true,
            "Determines if Hunters can use their Right-Click Attack");
        attackDrowsiness = hunterConfigData.Bind("HunterStats", "AttackStamina/DrowsinessDebuff", 0.5f,
            "The amount of Stamina Bar needed when the Hunter uses their Attack and amount of Drownsiness Applied");
        attackKnockbackMultiplier = hunterConfigData.Bind("HunterStats", "AttackKnockbackMultiplier", 2f,
            "Modifies the amount of Knockback received when within range of the Hunter Attack");
        attackType = hunterConfigData.Bind("HunterStats", "AttackType", "Curse",
            "The type of Afflication that can be received by Runners when within range of the Hunter Attack. " +
            "[Injury, Hunger, Cold, Poison, Crab, Curse, Drowzy, Weight, Hot, Thorns]");
        attackAmount = hunterConfigData.Bind("HunterStats", "AttackMaxAffliction", .1f,
            "The amount of Max Afflication Amount that can be received when within range of the Hunter Attack");
        Log.LogDebug("Config File Created");

        //Load AssetBundle
        string sAssemblyLocation = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "assets");
        assets = AssetBundle.LoadFromFile(Path.Combine(sAssemblyLocation, "peak-hunter"));
        //Load Assets
        Texture2D climber_tex = assets.LoadAsset<Texture2D>("Climber_Icon");
        Texture2D hunter_tex = assets.LoadAsset<Texture2D>("Hunter_Icon");
        climberSprite = Sprite.Create(climber_tex, new Rect(0, 0, climber_tex.width, climber_tex.height), new Vector2(0.5f, 0.5f));
        hunterSprite = Sprite.Create(hunter_tex, new Rect(0, 0, hunter_tex.width, hunter_tex.height), new Vector2(0.5f, 0.5f));
        Log.LogDebug("Assets Loaded");

        //Add Patches to Harmony
        Harmony.CreateAndPatchAll(typeof(Plugin));
        Log.LogDebug("Methods Patched");

        // Log our awake here so we can see it in LogOutput.log file
        Log.LogInfo($"Plugin {Name} is loaded!");
    }

    //Unload assets on Exit
    private void OnDestroy()
    {
        assets.Unload(true);
    }

    //---TESTING---
    /*private void Update()
    {
        //Spawn to next Target
        if (Input.GetKeyDown(KeyCode.G))
        {
            Log.LogDebug("Debug - Warping to next campfire");
            hunterCooldown = Time.time;
            Character.localCharacter.photonView.RPC("WarpPlayerRPC", RpcTarget.All, RespawnCharacterPos(true), true);
        }

        //Determine if player is Hunter
        if (Input.GetKeyDown(KeyCode.F))
        {
            if (isLocalHunter())
            {
                Log.LogDebug("Debug - Applied Status to Hunter");
                Character.localCharacter.refs.afflictions.AddStatus(CharacterAfflictions.STATUSTYPE.Poison, 1);
            }
        }

        //if (!playersReadyForHunter())
        //    Log.LogDebug("Players Not Ready");
    }*/

    //Add Database Updater Code on Photon Player
    [HarmonyPatch(typeof(Character), nameof(Character.Awake))]
    [HarmonyPostfix]
    private static void AddHunterUpdaterPatch(Character __instance)
    {
        __instance.gameObject.AddComponent<HunterPlayerUpdater>();

        //Load Config Data
        SyncPlayerData(__instance);

        //Add if player is loaded/ready to begin Hunter scene
        if (SceneManager.GetActiveScene().name == "Airport")
            changeCharacterReady(__instance, false);
        else
            changeCharacterReady(__instance, true);

        //Add SmallIcon to bottom left
        if (__instance.IsLocal)
        {
            smallRoleIcon = new GameObject("UI_RoleIcon", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image)).GetComponent<Image>();
            smallRoleIcon.transform.SetParent(GUIManager.instance.transform.Find("Canvas_HUD/BarGroup/Bar"));
            smallRoleIcon.transform.localScale = Vector3.one * .5f;
            smallRoleIcon.GetComponent<RectTransform>().anchoredPosition = new Vector2(-275, 55);

            smallRoleIcon.sprite = isLocalHunter() ? hunterSprite : climberSprite;
            Log.LogDebug("Small Role Icon added to HUD");
        }
    }

    private static void SyncPlayerData(Character character)
    {
        //Reset Static Values
        if (character.IsLocal && SceneManager.GetActiveScene().name == "Airport")
        {
            playersReady.Clear();
            randomBlowgunRunner = -1;
            hunterDatabase.Clear();
            hunterCooldown = -10000;
            roleSwitcher = null;
            boardingPass = null;
            Log.LogDebug("RESETTING STATIC VALUES");
        }

        if (!PhotonNetwork.IsMasterClient)
            return;

        //Send all data
        if (SceneManager.GetActiveScene().name == "Airport")
            foreach (Character charac in Character.AllCharacters)
                character.view.RPC("RPCA_ChangeRole", RpcTarget.Others, charac.view.Owner.ActorNumber, isHunter(charac));

        foreach (KeyValuePair<ConfigDefinition, ConfigEntryBase> configEntry in _.hunterConfigData)
            character.photonView.RPC("RPC_RecieveConfigData", RpcTarget.Others, configEntry.Key.Section, configEntry.Key.Key, configEntry.Value.BoxedValue);

        Log.LogDebug("Server: Sent All Config Info");
    }

    //Add The Hunter Is Near to GUI
    [HarmonyPatch(typeof(GUIManager), nameof(GUIManager.Awake))]
    [HarmonyPostfix]
    private static void HunterIsNearPatch(GUIManager __instance)
    {
        hunterNearPrefab = Instantiate(__instance.fogRises, __instance.fogRises.transform.parent);
        hunterNearPrefab.name = "Notification_Hunter";
        hunterNearPrefab.transform.Find("Fog").GetComponent<LocalizedText>().enabled = false;
        Log.LogDebug("Hunter Near UI Prefab-ed");
    }

    //Setup Passport UI
    [HarmonyPatch(typeof(PassportManager), nameof(PassportManager.Initialize))]
    [HarmonyPostfix]
    private static void PassportUIPatch(PassportManager __instance)
    {
        //Base Element
        GameObject roleUIElement = Instantiate(__instance.transform.Find("PassportUI/Canvas/Panel/Panel/BG/UI_Close").gameObject);
        roleUIElement.name = "UI_Role";

        //Positioning
        roleUIElement.transform.SetParent(__instance.transform.Find("PassportUI/Canvas/Panel/Panel/BG/Portrait"));
        RectTransform transform = roleUIElement.GetComponent<RectTransform>();
        transform.localScale = Vector3.one * .66f;
        transform.anchorMin = new Vector2(0, 0);
        transform.anchorMax = new Vector2(1, 0);
        transform.pivot = new Vector2(0.5f, 0);
        transform.anchoredPosition = new Vector2(0, 15);
        transform.sizeDelta = new Vector2(0, -10);
        //Box
        transform = transform.Find("Box").GetComponent<RectTransform>();
        transform.anchorMin = new Vector2(0, 0);
        transform.anchorMax = new Vector2(1, 1);

        //Modify
        Destroy(roleUIElement.GetComponent<Button>());
        Destroy(roleUIElement.transform.Find("Box/Icon").gameObject);
        roleLabel = new GameObject("Title", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI)).GetComponent<TextMeshProUGUI>();
        roleLabel.transform.SetParent(roleUIElement.transform.Find("Box"));
        roleLabel.transform.localScale = Vector3.one;
        //Positioning Text
        transform = roleLabel.GetComponent<RectTransform>();
        transform.anchorMin = new Vector2(0.5f, 0);
        transform.anchorMax = new Vector2(0.5f, 1);
        transform.anchoredPosition = new Vector2(-25, -25);
        //Modify Text
        roleLabel.text = isLocalHunter() ? "HUNTER" : "CLIMBER";
        roleLabel.fontSize = 29;
        roleLabel.horizontalAlignment = HorizontalAlignmentOptions.Center;
        TextMeshProUGUI exampleText = __instance.transform.Find("PassportUI/Canvas/Panel/Panel/BG/Text/Name/Text").GetComponent<TextMeshProUGUI>();
        roleLabel.font = exampleText.font;
        roleLabel.color = exampleText.color;

        //Add Switch Button
        roleSwitcher = Instantiate(__instance.transform.Find("PassportUI/Canvas/Panel/Panel/BG/UI_Close").gameObject).GetComponent<Button>();
        roleSwitcher.name = "UI_SwitchRole";

        //Positioning
        roleSwitcher.transform.SetParent(roleUIElement.transform);
        transform = roleSwitcher.GetComponent<RectTransform>();
        transform.localScale = Vector3.one;
        transform.anchorMin = new Vector2(1, 0.5f);
        transform.anchorMax = new Vector2(1, 0.5f);
        transform.pivot = new Vector2(1, 0.5f);
        transform.anchoredPosition = new Vector2(0, 0);
        transform.sizeDelta = new Vector2(0, 0);
        //Box
        transform = transform.Find("Box").GetComponent<RectTransform>();
        transform.anchorMin = new Vector2(0, 0);
        transform.anchorMax = new Vector2(1, 1);

        //Modify
        roleSwitcher.transform.Find("Box/Icon").GetComponent<RawImage>().texture = assets.LoadAsset<Texture2D>(isLocalHunter() ? "Hunter_Icon" : "Climber_Icon");
        roleSwitcher.transform.Find("SFX Click").GetComponent<SFX_PlayOneShot>().sfxs =
            __instance.transform.Find("PassportUI/Canvas/Panel/Panel/BG/Options/Grid/UI_PassportGridButton/SFX Click").GetComponent<SFX_PlayOneShot>().sfxs;
        roleSwitcher.onClick = new Button.ButtonClickedEvent();
        roleSwitcher.onClick.AddListener(() => ChangeRole());

        //Done
        Log.LogDebug("Passport UI Modified");
    }

    //Boarding Pass UI Icons
    [HarmonyPatch(typeof(BoardingPass), nameof(BoardingPass.OnOpen))]
    [HarmonyPostfix]
    private static void BoardingPassUIPatch(BoardingPass __instance)
    {
        //Set corresponding sprites
        for (int i = 0; i < Character.AllCharacters.Count; i++)
            __instance.players[i].sprite = isHunter(Character.AllCharacters[i]) ? hunterSprite : climberSprite;
        //Log BoardingPass instance for later updating
        if (boardingPass == null)
            boardingPass = __instance;
        Log.LogDebug("Boarding Pass UI Modified");
    }

    //Click to Change Roles
    private static void ChangeRole()
    {
        //Change Role
        bool localIsHunter = !isLocalHunter();
        Character.localCharacter.view.RPC("RPCA_ChangeRole", RpcTarget.All, Character.localCharacter.view.Owner.ActorNumber, localIsHunter);

        //Modify UI
        smallRoleIcon.sprite = localIsHunter ? hunterSprite : climberSprite;
        if (roleSwitcher != null)
        {
            roleSwitcher.transform.Find("Box/Icon").GetComponent<RawImage>().texture = assets.LoadAsset<Texture2D>(localIsHunter ? "Hunter_Icon" : "Climber_Icon");
            roleLabel.text = localIsHunter ? "HUNTER" : "CLIMBER";
            EventSystem.current.SetSelectedGameObject(null);
        }

        //Done
        Log.LogDebug("Role Changed -> " + (localIsHunter ? "HUNTER" : "CLIMBER"));
    }

    //Allows lighting of campfire without Hunter
    [HarmonyPatch(typeof(Campfire), nameof(Campfire.EveryoneInRange))]
    [HarmonyPrefix]
    private static bool CampfireWithoutHunterPatch(Campfire __instance, ref bool __result, out string printout)
    {
        //Edited from Campfire.EveryoneInRange()
        bool flag = true;
        printout = "";
        foreach (Character allPlayerCharacter in PlayerHandler.GetAllPlayerCharacters())
        {
            //Skip Hunters in Check
            if (!(allPlayerCharacter == null) && !allPlayerCharacter.photonView.Owner.IsInactive &&
                !isHunter(allPlayerCharacter))
            {
                float num = Vector3.Distance(__instance.transform.position, allPlayerCharacter.Center);
                if (num > 15f && !allPlayerCharacter.data.dead)
                {
                    flag = false;
                    printout += $"\n{allPlayerCharacter.photonView.Owner.NickName} {Mathf.RoundToInt(num * CharacterStats.unitsToMeters)}m";
                }
            }
        }
        if (!flag)
        {
            printout = LocalizedText.GetText("CANTLIGHT") + "\n" + printout;
        }
        __result = flag;
        //Do not run original code
        return false;
    }

    [HarmonyPatch(typeof(AirportCheckInKiosk), nameof(AirportCheckInKiosk.LoadIslandMaster))]
    [HarmonyPostfix]
    private static void HunterRandomizerPatch(AirportCheckInKiosk __instance)
    {
        if (!PhotonNetwork.IsMasterClient)
            return;

        if (_.pickRandom.Value)
        {
            //Reset
            foreach (Character character in Character.AllCharacters)
                character.view.RPC("RPCA_ChangeRole", RpcTarget.All, Character.localCharacter.view.Owner.ActorNumber, false);

            //Randomizes who is the Hunter
            int chosen = Random.Range(0, Character.AllCharacters.Count);
            Character.AllCharacters[chosen].view.RPC("RPCA_ChangeRole", RpcTarget.All, Character.localCharacter.view.Owner.ActorNumber, true);

            Log.LogDebug("Server: Chosen Random Hunter");
        }

        //Randomizes who gets the Blowgun
        Log.LogDebug("Runners Total: " + (Character.AllCharacters.Count - hunterDatabase.Count));
        if (Character.AllCharacters.Count - hunterDatabase.Count > 0)
        {
            //Randomizes till picks one who isn't a hunter
            int chosen2;
            do
            {
                chosen2 = Random.Range(0, Character.AllCharacters.Count);
            }
            while (isHunter(Character.AllCharacters[chosen2]));

            randomBlowgunRunner = Character.AllCharacters[chosen2].view.Owner.ActorNumber;
            Log.LogDebug("Server: Chosen Random Blowgun Runner -> " + randomBlowgunRunner + ": " + Character.AllCharacters[chosen2].characterName);
        }
    }

    //Conditions to spawn Hunter
    [HarmonyPatch(typeof(RunManager), nameof(RunManager.StartRun))]
    [HarmonyPatch(typeof(Campfire), nameof(Campfire.Light_Rpc))]
    [HarmonyPostfix]
    private static void ReachedNextStagePatch()
    {
        //Activate only if not in lobby
        if (SceneManager.GetActiveScene().name != "Airport")
        {
            //Reset
            if (MapHandler.Instance.currentSegment == 0)
            {
                //Climbers start with blowdart
                Character.localCharacter.view.RPC("RPC_SpawnBlowgun", RpcTarget.MasterClient, Character.localCharacter.view.Owner.ActorNumber, false);
            }
            //Load Section w/ Hunter Cooldown
            _.StartCoroutine(_.LoadNewStage());
        }
        //Spawn with blowgun in lobby
        else
            Character.localCharacter.view.RPC("RPC_SpawnBlowgun", RpcTarget.MasterClient, Character.localCharacter.view.Owner.ActorNumber, true);
    }

    //When first spawned and at each campfire
    private IEnumerator LoadNewStage()
    {
        int currSegment = MapHandler.Instance.currentSegment;
        if (currSegment == 0)
            yield return new WaitForSeconds(5);

        //Display The Hunter Is Near
        StartCoroutine(showMessage(isLocalHunter() ? "COOLDOWN ACTIVE" : "THE HUNTER IS NEAR..."));

        if (isLocalHunter())
        {
            CharacterAfflictions afflictions = Character.localCharacter.refs.afflictions;

            //Hunter Cooldown
            float cooldownLength = initialCooldown.Value + additionalCooldown.Value * MapHandler.Instance.currentSegment;
            hunterCooldown = cooldownLength + Time.time;
            // Refresh except Curse
            afflictions.ClearAllStatus();
            // Give Poison til ready
            afflictions.AddStatus(CharacterAfflictions.STATUSTYPE.Poison, 1);
            afflictions.lastAddedStatus[(int)CharacterAfflictions.STATUSTYPE.Poison] = float.PositiveInfinity;

            // Wait
            Log.LogDebug("Hunter Cooldown: " + cooldownLength);
            // Extra wait if people still passed out on beach
            while (currSegment == 0 && !playersReadyForHunter())
            {
                Log.LogDebug("Waiting for Players to Load on Beach");
                yield return null;
            }
            yield return new WaitForSeconds(cooldownLength);

            //Spawn Hunter
            hunterCooldown = Time.time;
            Character.localCharacter.photonView.RPC("WarpPlayerRPC", RpcTarget.All, RespawnCharacterPos(false), true);

            // Refresh except Curse again in case fell or other afflictions
            //Character.localCharacter.refs.items.DropAllItems(true);
            afflictions.ClearAllStatus();
            afflictions.UpdateWeight();
            // Reset the Poison again
            afflictions.AddStatus(CharacterAfflictions.STATUSTYPE.Poison,
                Character.localCharacter.GetMaxStamina());
            // Set to heal immediately
            afflictions.lastAddedStatus[(int)CharacterAfflictions.STATUSTYPE.Poison] = 0;
            afflictions.currentDecrementalStatuses[(int)CharacterAfflictions.STATUSTYPE.Poison] = 0.025f;
        }
    }

    [HarmonyPatch(typeof(Character), nameof(Character.HandlePassedOut))]
    [HarmonyPostfix]
    private static void HunterCantDieOnCooldownPatch(Character __instance)
    {
        if (hunterCooldown > Time.time && isLocalHunter())
        {
            //Hunter can't die on cooldown
            __instance.data.deathTimer = 0;
        }
    }

    [HarmonyPatch(typeof(Character), nameof(Character.UpdateVariablesFixed))]
    [HarmonyPostfix]
    private static void DisableHunterDeathTimer(Character __instance)
    {
        //Add back the timer amount
        if (isHunter(__instance) && __instance.data.fullyPassedOut && !__instance.data.carrier)
        {
            __instance.data.deathTimer -= Time.fixedDeltaTime / 60f;
        }
    }

    [HarmonyPatch(typeof(Character), nameof(Character.CheckEndGame))]
    [HarmonyPrefix]
    private static bool HunterDeathPatch(Character __instance)
    {
        //Modified from original CheckEndGame Function
        bool flag = true;
        for (int i = 0; i < Character.AllCharacters.Count; i++)
        {
            //Skip Hunters in Check
            if (!Character.AllCharacters[i].data.dead && !isHunter(Character.AllCharacters[i]))
            {
                flag = false;
            }
        }
        //Debug: Do not end game
        //flag = false;
        if (flag)
        {
            if (PhotonNetwork.IsMasterClient)
                __instance.EndGame();
            return false;
        }

        //Only do death scenarios on Specific Client that has just died
        if (!__instance.IsLocal)
            return false;

        //Immediate Respawn of Hunter if game continues
        if (isLocalHunter())
        {
            _.StartCoroutine(waitForRespawn());
            IEnumerator waitForRespawn()
            {
                yield return new WaitForSeconds(5);
                Log.LogDebug("Hunter Respawned");
                Character.localCharacter.refs.afflictions.UpdateWeight();
                hunterCooldown = Time.time;
                Character.localCharacter.photonView.RPC("RPCA_ReviveAtPosition", RpcTarget.All, RespawnCharacterPos(false), false);
                //Custom Effects
                Character.localCharacter.refs.afflictions.AddStatus(CharacterAfflictions.STATUSTYPE.Curse, 0.05f);
                Character.localCharacter.refs.afflictions.AddStatus(CharacterAfflictions.STATUSTYPE.Poison, 0.3f);
            }
        }
        else if (_.zombieMode.Value)
        {
            //Put on Hunter's team
            _.StartCoroutine(waitForRespawn());
            IEnumerator waitForRespawn()
            {
                yield return new WaitForSeconds(5);
                ChangeRole();
                Log.LogDebug("Climber Respawned as Hunter");
                Character.localCharacter.refs.afflictions.UpdateWeight();
                hunterCooldown = Time.time;
                Character.localCharacter.photonView.RPC("RPCA_ReviveAtPosition", RpcTarget.All, RespawnCharacterPos(false), false);
                //Custom Effects
                Character.localCharacter.refs.afflictions.AddStatus(CharacterAfflictions.STATUSTYPE.Curse, 0.05f);
                Character.localCharacter.refs.afflictions.AddStatus(CharacterAfflictions.STATUSTYPE.Poison, 0.3f);
                //Show User that they are now a Hunter
                _.StartCoroutine(_.showMessage("YOU ARE NOW A HUNTER"));
            }
        }
        //Don't return to original method
        return false;
    }

    //Runners can't spectate Hunters
    [HarmonyPatch(typeof(MainCameraMovement), nameof(MainCameraMovement.SwapSpecPlayer))]
    [HarmonyPrefix]
    private static bool CantSpectateHunterPatch(MainCameraMovement __instance, int add)
    {
        //Modified from SwapSecPlayer function
        List<Character> list = new List<Character>();
        foreach (Character allPlayerCharacter in PlayerHandler.GetAllPlayerCharacters())
        {
            //Remove Hunters from List unless local is Hunter
            if (!allPlayerCharacter.data.dead && !allPlayerCharacter.isBot && (isLocalHunter() || !isHunter(allPlayerCharacter)))
            {
                list.Add(allPlayerCharacter);
            }
        }

        if (list.Count == 0)
        {
            MainCameraMovement.specCharacter = null;
            return false;
        }

        if (MainCameraMovement.specCharacter == null)
        {
            Debug.LogError("WE FOUND IT");
            return false;
        }

        int playerListID = MainCameraMovement.specCharacter.GetPlayerListID(list);
        playerListID += add;
        if (playerListID < 0)
        {
            playerListID = list.Count - 1;
        }

        if (playerListID >= list.Count)
        {
            playerListID = 0;
        }

        MainCameraMovement.specCharacter = list[playerListID];

        //Don't return to original method
        return false;
    }

    private static Vector3 RespawnCharacterPos(bool nextSection)
    {
        int sectionNum = MapHandler.Instance.currentSegment;
        if (nextSection)
            sectionNum++;

        // Spawn Location
        Vector3 spawnPosition;
        switch ((Segment)sectionNum)
        {
            case Segment.Peak:
                spawnPosition = MapHandler.Instance.respawnThePeak.position;
                break;
            default:
                spawnPosition = MapHandler.Instance.segments[sectionNum].reconnectSpawnPos.position;
                break;
        }
        return spawnPosition;
    }

    [HarmonyPatch(typeof(Character), nameof(Character.ClampStamina))]
    [HarmonyPostfix]
    private static void ExtraStaminaPatch(Character __instance)
    {
        if (!__instance.CanRegenStamina())
            return;

        if (isLocalHunter())
        {
            if (SceneManager.GetActiveScene().name == "Airport" && __instance.data.extraStamina > _.hunterExtraStamina.Value)
                __instance.SetExtraStamina(_.hunterExtraStamina.Value);
            if (__instance.data.extraStamina < _.hunterExtraStamina.Value)
                __instance.AddExtraStamina(Time.fixedDeltaTime * 0.1f);
        }
        else
        {
            if (SceneManager.GetActiveScene().name == "Airport" && __instance.data.extraStamina > _.climberExtraStamina.Value)
                __instance.SetExtraStamina(_.climberExtraStamina.Value);
            if (__instance.data.extraStamina < _.climberExtraStamina.Value)
                __instance.AddExtraStamina(Time.fixedDeltaTime * 0.1f);
        }
    }

    [HarmonyPatch(typeof(CharacterAfflictions), nameof(CharacterAfflictions.AddStatus))]
    [HarmonyPrefix]
    private static void ModifyHealthPatch(CharacterAfflictions __instance, CharacterAfflictions.STATUSTYPE statusType, ref float amount, bool fromRPC)
    {
        if (statusType != CharacterAfflictions.STATUSTYPE.Injury)
            return;
        if (isHunter(__instance.character))
        {
            amount *= _.hunterDamageMultiplier.Value;
            //Null fall damage for 7 seconds after respawning
            if (hunterCooldown - Time.time > -7)
                amount = 0;
            Log.LogDebug("Time since hunterCooldown: " + (hunterCooldown - Time.time));
        }
        else
            amount *= _.climberDamageMultiplier.Value;
        Log.LogDebug("Reduced Damage: " + amount);
    }

    [HarmonyPatch(typeof(CharacterGrabbing), nameof(CharacterGrabbing.RPCA_StartReaching))]
    [HarmonyPostfix]
    private static void HunterReachAttackPatch(CharacterGrabbing __instance)
    {
        if (!_.enableHunterAttack.Value)
            return;
        if (!isHunter(__instance.character) || __instance.character.GetMaxStamina() < _.attackDrowsiness.Value)
            return;
        //Summons area affect cloud that will push away and hurt anyone in vicinity
        Vector3 attackPos = __instance.character.Center + __instance.character.data.lookDirection * 1.5f;
        __instance.character.PlayPoofVFX(attackPos);

        foreach (Character character in Character.AllCharacters)
        {
            if (character == __instance.character)
                continue;

            float num = Vector3.Distance(attackPos, character.Center);
            if (num < 5)
            {
                //If climbing, doesn't ragdoll
                if (!character.data.isClimbing)
                    character.Fall(0.1f);
                character.AddForce((5 - num) * (character.Center - __instance.character.Center).normalized * 133 *
                    _.attackKnockbackMultiplier.Value);
                //Hunter doesn't get damaged
                if (!isHunter(character))
                {
                    CharacterAfflictions.STATUSTYPE affliction;
                    if (!System.Enum.TryParse(_.attackType.Value, false, out affliction))
                        affliction = CharacterAfflictions.STATUSTYPE.Injury;

                    float attackValue = (5 - num) / 5 * _.attackAmount.Value;
                    //Remove the damage multiplier for Hunter Attack
                    if (affliction == CharacterAfflictions.STATUSTYPE.Injury)
                        attackValue /= _.climberDamageMultiplier.Value;
                    character.refs.afflictions.AddStatus(affliction, attackValue);
                }
            }
        }

        //Seperate Force added to User
        __instance.character.Fall(0.1f);
        __instance.character.AddForce((5 - 1.5f) * (__instance.character.Center - attackPos).normalized * 133 *
            _.attackKnockbackMultiplier.Value);

        //Gives Drowsiness to Hunter
        __instance.character.refs.afflictions.AddStatus(CharacterAfflictions.STATUSTYPE.Drowsy, _.attackDrowsiness.Value);

        Log.LogDebug("Hunter Attack!");
    }

    [HarmonyPatch(typeof(Item), nameof(Item.GetItemName))]
    [HarmonyPostfix]
    private static void BlowgunRenamePatch(Item __instance, ItemInstanceData data, ref string __result)
    {
        //Sneaky way to track Reusable Blowgun
        ItemInstanceData itemData = data;
        if (itemData == null)
            itemData = __instance.data;
        OptionableIntItemData specificIntItemData;
        if (!itemData.TryGetDataEntry(DataEntryKey.INVALID, out specificIntItemData) || specificIntItemData.Value != 1)
            return;

        //Rename
        __result = "REUSABLE " + __result;

        return;
    }

    [HarmonyPatch(typeof(Item), nameof(Item.Consume))]
    [HarmonyPrefix]
    private static bool BlowgunReusePatch(Item __instance)
    {
        //Sneaky way to track Reusable Blowgun
        OptionableIntItemData specificIntItemData = __instance.GetData<OptionableIntItemData>(DataEntryKey.INVALID);
        if (!specificIntItemData.HasData || specificIntItemData.Value != 1)
            return true;

        __instance.SetUseRemainingPercentage(0);

        //Don't consume and instead wait to give use back
        _.StartCoroutine(_.ItemCooldown(__instance.data));
        Log.LogDebug("Climber Blowdart on Recharge");

        return false;
    }

    private IEnumerator ItemCooldown(ItemInstanceData itemData)
    {
        //Slowly Recharge
        FloatItemData regainedUsage;
        itemData.TryGetDataEntry(DataEntryKey.UseRemainingPercentage, out regainedUsage);
        while (regainedUsage.Value < 1 && itemData != null)
        {
            yield return new WaitForSeconds(1);
            regainedUsage.Value += 1f / (_.blowgunCooldown.Value * 60);
        }

        //Fully recharged
        if (itemData != null)
        {
            OptionableIntItemData itemUses;
            itemData.TryGetDataEntry(DataEntryKey.ItemUses, out itemUses);
            itemUses.Value = 1;
            itemData.data.Remove(DataEntryKey.UseRemainingPercentage);
            Log.LogDebug("Climber Blowdart Recharged");
        }
    }

    //Remove Scoutmaster
    [HarmonyPatch(typeof(ScoutmasterSpawner), nameof(ScoutmasterSpawner.SpawnScoutmaster))]
    [HarmonyPrefix]
    public static bool DisableScoutmasterPatch()
    {
        if (_.disableScoutmaster.Value)
        {
            Log.LogDebug("Disabled Scoutmaster Spawning");
            //Do not return to original function
            return false;
        }
        else
            return true;
    }

    //Make Ancient Statues not see Hunters as Dead
    [HarmonyPatch(typeof(Character), nameof(Character.PlayerIsDeadOrDown))]
    [HarmonyPrefix]
    private static bool HuntersNotDeadPatch(ref bool __result)
    {
        __result = false;

        //Modified from original PlayerIsDeadOrDown Function
        foreach (Character allCharacter in Character.AllCharacters)
        {
            //Dont count hunters
            if ((allCharacter.data.dead || allCharacter.data.fullyPassedOut) && !isHunter(allCharacter))
            {
                __result = true;
            }
        }

        //Do not return to original method
        return false;
    }

    //Only respawn those that are not Hunters
    [HarmonyPatch(typeof(RespawnChest), nameof(RespawnChest.RespawnAllPlayersHere))]
    [HarmonyPrefix]
    private static bool DontRespawnHuntersPatch(RespawnChest __instance)
    {
        //Modified from original RespawnAllPlayersHere Function
        foreach (Character allCharacter in Character.AllCharacters)
        {
            //Dont count hunters
            if ((allCharacter.data.dead || allCharacter.data.fullyPassedOut) && !isHunter(allCharacter))
            {
                allCharacter.photonView.RPC("RPCA_ReviveAtPosition", RpcTarget.All, __instance.transform.position + __instance.transform.up * 8f, true);
            }
        }

        //Do not return to original method
        return false;
    }

    //Hunters cannot interact with Ancient Statues (for balancing)
    [HarmonyPatch(typeof(RespawnChest), nameof(RespawnChest.IsInteractible))]
    [HarmonyPrefix]
    private static bool HunterNotUseStatuePatch(RespawnChest __instance, Character interactor, ref bool __result)
    {
        if (isHunter(interactor))
        {
            __result = false;
            //Do not return to original method
            return false;
        }
        return true;
    }

    //Only instantiate once
    [HarmonyPatch(typeof(SharedSettingsMenu), nameof(SharedSettingsMenu.OnEnable))]
    [HarmonyPrefix]
    private static void AddedHunterMenuPatch(SharedSettingsMenu __instance)
    {
        //Add tab
        if (hunterTab == null)
        {
            //Create Tab
            hunterTab = Instantiate(__instance.transform.Find("Content/TABS/General").gameObject, __instance.transform.Find("Content/TABS")).GetComponent<SettingsTABSButton>();
            hunterTab.name = "Hunter";
            hunterTab.text.GetComponent<LocalizedText>().enabled = false;
            hunterTab.text.text = "HUNTER MOD";
            __instance.m_tabs.AddButton(hunterTab);
            //Resize Content to Resize Later
            __instance.transform.Find("Content/Parent").GetComponent<RectTransform>().pivot = new Vector2(0.5f, 1);
            __instance.transform.Find("Content/Parent").GetComponent<RectTransform>().anchoredPosition = new Vector2(0, -62);
            Log.LogDebug("Added Hunter Mod Tab");
        }
    }

    [HarmonyPatch(typeof(SharedSettingsMenu), nameof(SharedSettingsMenu.RefreshSettings))]
    [HarmonyPrefix]
    private static bool HunterMenuSettingsPatch(SharedSettingsMenu __instance)
    {
        if (__instance.m_tabs.selectedButton != hunterTab)
        {
            //Reset Size
            __instance.transform.Find("Content/Parent").localScale = Vector3.one * 1;
            return true;
        }

        //Resize to fit all
        __instance.transform.Find("Content/Parent").localScale = Vector3.one * 0.52f;

        //Load Settings
        __instance.settings.Clear();

        //Profiles
        __instance.settings.Add(new HunterSettingProfiles());

        string lastSection = "";
        foreach (KeyValuePair<ConfigDefinition, ConfigEntryBase> configEntry in _.hunterConfigData)
        {
            if (configEntry.Key.Section != lastSection)
            {
                __instance.settings.Add(new HunterCategorySetting(configEntry.Key.Section));
                lastSection = configEntry.Key.Section;
            }
            switch (configEntry.Value.BoxedValue)
            {
                case float f:
                    __instance.settings.Add(new HunterNumSetting(configEntry.Key, configEntry.Value, false));
                    break;
                case int i:
                    __instance.settings.Add(new HunterNumSetting(configEntry.Key, configEntry.Value, true));
                    break;
                case bool b:
                    __instance.settings.Add(new HunterBoolSetting(configEntry.Key, (ConfigEntry<bool>)configEntry.Value));
                    break;
                case string s:
                    __instance.settings.Add(new HunterEnumSetting(configEntry.Key, (ConfigEntry<string>)configEntry.Value));
                    break;
            }
        }

        Log.LogDebug("First Pass: Loaded Hunter Tab Options");
        return false;
    }

    [HarmonyPatch(typeof(SharedSettingsMenu), nameof(SharedSettingsMenu.ShowSettings))]
    [HarmonyPostfix]
    private static void HunterMenuSettingsPostPatch(SharedSettingsMenu __instance)
    {
        if (__instance.m_tabs.selectedButton != hunterTab)
            return;

        //Categories
        Transform content = __instance.transform.Find("Content/Parent");
        int category = 0;
        foreach (EnumSettingUI enumSetting in content.GetComponentsInChildren<EnumSettingUI>())
            if (enumSetting.GetComponentInParent<SettingsUICell>().m_text.text.Contains("CATEGORY"))
            {
                enumSetting.GetComponentInParent<SettingsUICell>().m_text.fontSizeMax = 40;
                switch (category)
                {
                    case 0: enumSetting.GetComponentInParent<SettingsUICell>().m_text.color = new Color(.750f, .594f, .188f); break;
                    case 1: enumSetting.GetComponentInParent<SettingsUICell>().m_text.color = new Color(.188f, .458f, .750f); break;
                    case 2: enumSetting.GetComponentInParent<SettingsUICell>().m_text.color = new Color(.750f, .192f, .238f); break;
                }
                enumSetting.gameObject.SetActive(false);
                category++;
            }

        //Remove "LOC: "
        foreach (LocalizedText locText in __instance.transform.Find("Content/Parent").GetComponentsInChildren<LocalizedText>())
        {
            locText.enabled = false;
            locText.tmp.text = locText.currentText.Replace("LOC: ", "");
        }

        //Change cannot change text
        for (int i = 0; i < content.childCount; i++)
            content.GetChild(i).Find("OnlyOnMainMenu").GetComponent<TextMeshProUGUI>().text = "THESE SETTINGS CAN ONLY BE ADJUSTED BY THE HOST.";

        //Disable Rest
        if (Player.localPlayer != null && !PhotonNetwork.IsMasterClient)
            foreach (Selectable ui in __instance.transform.Find("Content/Parent").GetComponentsInChildren<Selectable>())
                ui.interactable = false;

        Log.LogDebug("Second Pass: Loaded Hunter Tab Options");
    }

    //BTW - Function does not work (I think works better now?)
    private void UpdateHunterModSettings()
    {
        //Not loaded
        if (hunterTab == null)
            return;

        //Not on same tab
        SharedSettingsMenu menu = hunterTab.GetComponentInParent<SharedSettingsMenu>();
        Log.LogDebug("Failed to load Hunter Tab -> " + (menu == null || menu.m_tabs == null || menu.m_tabs.selectedButton == null || menu.m_tabs.selectedButton != hunterTab));
        if (menu == null || menu.m_tabs == null || menu.m_tabs.selectedButton == null || menu.m_tabs.selectedButton != hunterTab)
            return;

        //Refesh values
        Transform content = menu.transform.Find("Content/Parent");
        for (int i = 1; i < content.childCount; i++)
        {
            if (content.GetChild(i).GetComponentInChildren<FloatSettingUI>() is FloatSettingUI floatComponent)
            {
                floatComponent.slider.SetValueWithoutNotify(((HunterNumSetting)menu.settings[i]).GetValue());
                floatComponent.inputField.SetTextWithoutNotify(((HunterNumSetting)menu.settings[i]).Expose(floatComponent.slider.value));
            }
            else if (content.GetChild(i).GetComponentInChildren<EnumSettingUI>() is EnumSettingUI enumComponent)
            {
                if ((menu.settings[i] as HunterBoolSetting) != null)
                    enumComponent.dropdown.SetValueWithoutNotify(((HunterBoolSetting)menu.settings[i]).GetValue());
                else if ((menu.settings[i] as HunterEnumSetting) != null)
                    enumComponent.dropdown.SetValueWithoutNotify(((HunterEnumSetting)menu.settings[i]).GetValue());
            }
        }

        UpdateProfileSetting();
        Log.LogDebug("Hunter Tab Updated");
    }

    private int UpdateProfileSetting()
    {
        SharedSettingsMenu menu = hunterTab.GetComponentInParent<SharedSettingsMenu>();
        Transform content = menu.transform.Find("Content/Parent");
        EnumSettingUI enumComponent = content.GetChild(0).GetComponentInChildren<EnumSettingUI>();

        //Check if the values are modified
        bool modifiedValues = false;
        foreach (KeyValuePair<ConfigDefinition, ConfigEntryBase> configEntry in _.hunterConfigData)
        {
            switch (configEntry.Value.BoxedValue)
            {
                case float f:
                    if (f != (float)configEntry.Value.DefaultValue)
                        modifiedValues = true;
                    break;
                case int i:
                    if (i != (int)configEntry.Value.DefaultValue)
                        modifiedValues = true;
                    break;
                case bool b:
                    if (b != (bool)configEntry.Value.DefaultValue)
                        modifiedValues = true;
                    break;
                case string s:
                    if (s != (string)configEntry.Value.DefaultValue)
                        modifiedValues = true;
                    break;
            }
        }

        int value = modifiedValues ? 0 : 1;
        enumComponent.dropdown.SetValueWithoutNotify(value);
        return value;
    }

    enum SettingProfiles
    {
        Custom,
        OneHunter
    }

    class HunterSettingProfiles : EnumSetting<SettingProfiles>, IExposedSetting, IConditionalSetting
    {
        public HunterSettingProfiles()
        {
            Value = (SettingProfiles)_.UpdateProfileSetting();
        }

        public override void ApplyValue()
        {
            if (Value == SettingProfiles.OneHunter)
            {
                foreach (KeyValuePair<ConfigDefinition, ConfigEntryBase> configEntry in _.hunterConfigData)
                {
                    configEntry.Value.BoxedValue = configEntry.Value.DefaultValue;
                    if (Character.localCharacter != null)
                        Character.localCharacter.view.RPC("RPC_RecieveConfigData", RpcTarget.Others, configEntry.Key.Section, configEntry.Key.Key, configEntry.Value.DefaultValue);
                }
                _.UpdateHunterModSettings();
            }
        }

        public string GetCategory()
        {
            return "General";
        }

        public string GetDisplayName()
        {
            return "Setting Profiles";
        }

        public override List<LocalizedString> GetLocalizedChoices()
        {
            return null;
        }

        public override List<string> GetUnlocalizedChoices()
        {
            return new List<string>() { "Custom", "1 Hunter" };
        }

        public bool ShouldShow()
        {
            return Player.localPlayer == null || PhotonNetwork.IsMasterClient;
        }

        protected override SettingProfiles GetDefaultValue()
        {
            return SettingProfiles.Custom;
        }
    }

    class HunterCategorySetting : OffOnSetting, IExposedSetting
    {
        string categoryName;

        public HunterCategorySetting(string categoryName)
        {
            this.categoryName = categoryName;
        }

        public override void ApplyValue()
        {
        }

        public string GetCategory()
        {
            return "General";
        }

        public string GetDisplayName()
        {
            return "Category: " + Regex.Replace(categoryName, "(?!^)([A-Z])", " $1").Replace("_", "");
        }

        public override List<LocalizedString> GetLocalizedChoices()
        {
            return null;
        }

        protected override OffOnMode GetDefaultValue()
        {
            return OffOnMode.OFF;
        }
    }

    class HunterNumSetting : FloatSetting, IExposedSetting
    {
        bool isInt;
        ConfigDefinition configDef;
        ConfigEntryBase config;

        public HunterNumSetting(ConfigDefinition configDef, ConfigEntryBase config, bool isInt)
        {
            this.isInt = isInt;
            this.configDef = configDef;
            this.config = config;
            Value = isInt ? (int)config.BoxedValue : (float)config.BoxedValue;
            //Load Method
            Unity.Mathematics.float2 minMaxValue = GetMinMaxValue();
            MinValue = minMaxValue.x;
            MaxValue = minMaxValue.y;
        }

        public override void ApplyValue()
        {
            if (isInt)
                ((ConfigEntry<int>)config).Value = (int)Value;
            else
                ((ConfigEntry<float>)config).Value = Value;
            if (Character.localCharacter != null)
            {
                Character.localCharacter.view.RPC("RPC_RecieveConfigData", RpcTarget.Others, configDef.Section, configDef.Key, config.BoxedValue);
                Log.LogDebug("Server: Sent Config Info");
            }
            _.UpdateProfileSetting();
        }

        public float GetValue()
        {
            return isInt ? (int)config.BoxedValue : (float)config.BoxedValue;
        }

        public string GetCategory()
        {
            return "General";
        }

        public string GetDisplayName()
        {
            return Regex.Replace(configDef.Key, "(?!^)([A-Z])", " $1");
        }

        protected override float GetDefaultValue()
        {
            return (float)(isInt ? (int)config.DefaultValue : config.DefaultValue);
        }

        protected override Unity.Mathematics.float2 GetMinMaxValue()
        {
            switch (configDef.Key)
            {
                case "InitialHunterCooldown":
                case "AddedCooldownPerSection":
                    return new Unity.Mathematics.float2(0, 100);
                case "FallDamageMultiplier":
                    return new Unity.Mathematics.float2(0, 2);
                case "BlowgunCooldownInMins":
                    return new Unity.Mathematics.float2(0, 20);
                case "AttackKnockbackMultiplier":
                    return new Unity.Mathematics.float2(0, 10);
                default:
                    return new Unity.Mathematics.float2(0, 1);
            }
        }
    }

    class HunterBoolSetting : OffOnSetting, IExposedSetting
    {
        ConfigDefinition configDef;
        ConfigEntry<bool> config;

        public HunterBoolSetting(ConfigDefinition configDef, ConfigEntry<bool> config)
        {
            this.configDef = configDef;
            this.config = config;
            Value = config.Value ? OffOnMode.ON : OffOnMode.OFF;
        }

        public override void ApplyValue()
        {
            config.Value = Value == OffOnMode.ON;
            if (Character.localCharacter != null)
            {
                Character.localCharacter.view.RPC("RPC_RecieveConfigData", RpcTarget.Others, configDef.Section, configDef.Key, config.Value);
                Log.LogDebug("Server: Sent Config Info");
            }
            _.UpdateProfileSetting();
        }

        public override int GetValue()
        {
            return (bool)config.BoxedValue ? 1 : 0;
        }

        public string GetCategory()
        {
            return "General";
        }

        public string GetDisplayName()
        {
            return Regex.Replace(configDef.Key, "(?!^)([A-Z])", " $1");
        }

        public override List<LocalizedString> GetLocalizedChoices()
        {
            return null;
        }

        protected override OffOnMode GetDefaultValue()
        {
            return (bool)config.DefaultValue ? OffOnMode.ON : OffOnMode.OFF;
        }
    }

    class HunterEnumSetting : EnumSetting<CharacterAfflictions.STATUSTYPE>, IExposedSetting
    {
        ConfigDefinition configDef;
        ConfigEntry<string> config;

        public HunterEnumSetting(ConfigDefinition configDef, ConfigEntry<string> config)
        {
            this.configDef = configDef;
            this.config = config;
            CharacterAfflictions.STATUSTYPE _value;
            if (System.Enum.TryParse(config.Value, false, out _value))
                Value = _value;
            else
                Value = CharacterAfflictions.STATUSTYPE.Injury;
        }

        public override void ApplyValue()
        {
            config.Value = Value.ToString();
            if (Character.localCharacter != null)
            {
                Character.localCharacter.view.RPC("RPC_RecieveConfigData", RpcTarget.Others, configDef.Section, configDef.Key, config.Value);
                Log.LogDebug("Server: Sent Config Info");
            }
            _.UpdateProfileSetting();
        }

        public override int GetValue()
        {
            CharacterAfflictions.STATUSTYPE value;
            if (System.Enum.TryParse((string)config.BoxedValue, false, out value))
                return (int)value;
            else
                return (int)CharacterAfflictions.STATUSTYPE.Injury;
        }

        public string GetCategory()
        {
            return "General";
        }

        public string GetDisplayName()
        {
            return Regex.Replace(configDef.Key, "(?!^)([A-Z])", " $1");
        }

        public override List<LocalizedString> GetLocalizedChoices()
        {
            return null;
        }

        public override List<string> GetUnlocalizedChoices()
        {
            return new List<string>() {"Injury", "Hunger", "Cold", "Poison", "Crab", "Curse", "Drowzy", "Weight", "Hot", "Thorns"};
        }

        protected override CharacterAfflictions.STATUSTYPE GetDefaultValue()
        {
            return CharacterAfflictions.STATUSTYPE.Injury;
        }
    }
}
