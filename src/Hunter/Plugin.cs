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

    //Hunter Data
    private static List<int> hunterDatabase = new List<int>();
    private static bool onHunterCooldown = false;
    ConfigFile hunterConfigData = new ConfigFile(Path.Combine(Paths.ConfigPath, "BT_Hunter.cfg"), true);
    ConfigEntry<float> initialCooldown;
    ConfigEntry<float> additionalCooldown;
    ConfigEntry<float> extraStamina;

    //Assets
    private static AssetBundle assets;
    private static Sprite climberSprite;
    private static Sprite hunterSprite;

    //General UI
    private static Image smallRoleIcon;
    private static GameObject hunterNearPrefab;
    //PassportUI
    private static GameObject roleUIElement;
    private static TextMeshProUGUI roleLabel;
    private static Button roleSwitcher;

    //Client to Server Updater
    public class HunterPlayerUpdater : MonoBehaviourPun
    {
        //Update on all clients
        [PunRPC]
        public void RPCA_ChangeRole(Character localCharacter)
        {
            if (hunterDatabase.Contains(localCharacter.view.Owner.ActorNumber))
                hunterDatabase.Remove(localCharacter.view.Owner.ActorNumber);
            else
                hunterDatabase.Add(localCharacter.view.Owner.ActorNumber);
        }
    }

    private static bool isLocalHunter()
    {
        return hunterDatabase.Contains(Character.localCharacter.view.Owner.ActorNumber);
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
        initialCooldown = hunterConfigData.Bind("Gamemode", "InitialHunterCooldown", 10f,
            "Change the Cooldown of how long the Hunter is knocked out");
        additionalCooldown = hunterConfigData.Bind("Gamemode", "AdditionalHunterCooldown", 10f,
            "Increases the amount of Cooldown applied after each Section");
        extraStamina = hunterConfigData.Bind("HunterStats", "ExtraStamina", 0.5f,
            "Applies this extra Stamina when the Hunter is rested");
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

    //---TESTING---
    /*private void Update()
    {
        //Spawn to next Target
        if (Input.GetKeyDown(KeyCode.G))
        {
            Log.LogDebug("Debug - Warping to next campfire");
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
    }*/

    //Add Database Updater Code on Photon Player
    [HarmonyPatch(typeof(Character), nameof(Character.Awake))]
    [HarmonyPostfix]
    private static void AddHunterUpdaterPatch(Character __instance)
    {
        __instance.gameObject.AddComponent<HunterPlayerUpdater>();

        //Add SmallIcon to bottom left
        smallRoleIcon = new GameObject("UI_RoleIcon", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image)).GetComponent<Image>();
        smallRoleIcon.transform.SetParent(GUIManager.instance.transform.Find("Canvas_HUD/BarGroup/Bar"));
        smallRoleIcon.transform.localScale = Vector3.one * .5f;
        smallRoleIcon.GetComponent<RectTransform>().anchoredPosition = new Vector2(-275, 55);

        smallRoleIcon.sprite = isLocalHunter() ? hunterSprite : climberSprite;
        Log.LogDebug("Small Role Icon added to HUD");
    }

    //Add The Hunter Is Near to GUI
    [HarmonyPatch(typeof(GUIManager), nameof(GUIManager.Awake))]
    [HarmonyPostfix]
    private static void HunterIsNearPatch(GUIManager __instance)
    {
        hunterNearPrefab = Instantiate(__instance.fogRises, __instance.fogRises.transform.parent);
        hunterNearPrefab.transform.Find("Fog").GetComponent<LocalizedText>().enabled = false;
        hunterNearPrefab.transform.Find("Fog").GetComponent<TextMeshProUGUI>().text = "THE HUNTER IS NEAR...";
        Log.LogDebug("Hunter Near UI Prefab-ed");
    }

    //Setup Passport UI
    [HarmonyPatch(typeof(PassportManager), nameof(PassportManager.Initialize))]
    [HarmonyPostfix]
    private static void PassportUIPatch(PassportManager __instance)
    {
        //Base Element
        roleUIElement = Instantiate(__instance.transform.Find("PassportUI/Canvas/Panel/Panel/BG/UI_Close").gameObject);
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
            __instance.players[i].sprite = hunterDatabase.Contains(Character.AllCharacters[i].view.Owner.ActorNumber) ? hunterSprite : climberSprite;
        Log.LogDebug("Boarding Pass UI Modified");
    }

    //Click to Change Roles
    private static void ChangeRole()
    {
        //Change Role
        bool localIsHunter = !isLocalHunter();
        Character.localCharacter.view.RPC("RPCA_ChangeRole", RpcTarget.AllBuffered, Character.localCharacter);

        //Modify UI
        smallRoleIcon.sprite = localIsHunter ? hunterSprite : climberSprite;
        roleSwitcher.transform.Find("Box/Icon").GetComponent<RawImage>().texture = assets.LoadAsset<Texture2D>(localIsHunter ? "Hunter_Icon" : "Climber_Icon");
        roleLabel.text = localIsHunter ? "HUNTER": "CLIMBER";
        EventSystem.current.SetSelectedGameObject(null);

        //Done
        Log.LogDebug("Role Changed");
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
                !hunterDatabase.Contains(allPlayerCharacter.photonView.Owner.ActorNumber))
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
                Log.LogDebug("Reset Section Progress");
                currSegment = -1;
            }
            //Load Section w/ Hunter Cooldown
            _.StartCoroutine(_.LoadNewStage());
        }
    }

    //When first spawned and at each campfire
    static int currSegment = -1;
    public IEnumerator LoadNewStage()
    {
        currSegment++;
        if (currSegment == 0)
            yield return new WaitForSeconds(5);

        //Display The Hunter Is Near
        StartCoroutine(showHunterTitle());
        IEnumerator showHunterTitle()
        {
            Log.LogDebug("Show Hunter is Near");
            GameObject hunterNear = Instantiate(hunterNearPrefab, hunterNearPrefab.transform.parent);
            hunterNear.SetActive(true);
            yield return new WaitForSeconds(4);
            Destroy(hunterNear);
        }

        if (isLocalHunter())
        {
            CharacterAfflictions afflictions = Character.localCharacter.refs.afflictions;

            //Hunter Cooldown
            onHunterCooldown = true;
            // Refresh except Curse
            afflictions.ClearAllStatus();
            // Give Poison til ready
            afflictions.AddStatus(CharacterAfflictions.STATUSTYPE.Poison, 1);
            afflictions.lastAddedStatus[(int)CharacterAfflictions.STATUSTYPE.Poison] = float.PositiveInfinity;

            // Wait
            float cooldownLength = initialCooldown.Value + additionalCooldown.Value * MapHandler.Instance.currentSegment;
            Log.LogDebug("Hunter Cooldown: " + cooldownLength);
            yield return new WaitForSeconds(cooldownLength);

            //Spawn Hunter
            onHunterCooldown = false;
            Character.localCharacter.photonView.RPC("WarpPlayerRPC", RpcTarget.All, RespawnCharacterPos(false), true);

            // Refresh except Curse again in case fell or other afflictions
            Character.localCharacter.refs.items.DropAllItems(true);
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
        if (onHunterCooldown && isLocalHunter())
        {
            //Hunter can't die on cooldown
            __instance.data.deathTimer = 0;
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
            if (!Character.AllCharacters[i].data.dead && !hunterDatabase.Contains(Character.AllCharacters[i].photonView.Owner.ActorNumber))
            {
                flag = false;
            }
        }
        //flag = false;
        if (flag)
        {
            if (PhotonNetwork.IsMasterClient)
                __instance.EndGame();
            return false;
        }

        //Immediate Respawn of Hunter if game continues
        if (isLocalHunter())
        {
            _.StartCoroutine(waitForRespawn());
            IEnumerator waitForRespawn()
            {
                yield return new WaitForSeconds(5);
                Log.LogDebug("Hunter Respawned");
                Character.localCharacter.refs.afflictions.UpdateWeight();
                Character.localCharacter.photonView.RPC("RPCA_ReviveAtPosition", RpcTarget.All, RespawnCharacterPos(false), true);
            }
        }
        //Don't return to original method
        return false;
    }

    private static Vector3 RespawnCharacterPos(bool nextSection)
    {
        int sectionNum = currSegment;
        if (nextSection)
            sectionNum++;

        // Spawn Location
        Vector3 spawnPosition;
        switch ((Segment)sectionNum)
        {
            case Segment.TheKiln:
                spawnPosition = MapHandler.Instance.respawnTheKiln.position;
                break;
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
    private static void HunterExtraStaminaPatch(Character __instance)
    {
        if (isLocalHunter())
        {
            if (__instance.data.currentStamina == __instance.GetMaxStamina() && __instance.data.extraStamina < _.extraStamina.Value)
                __instance.SetExtraStamina(_.extraStamina.Value);
        }
    }
}
