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
    private static bool onHunterCooldown = false;
    float initialCooldown = 10;
    float additionalCooldown = 10;
    static float extraStamina = .5f;

    //Assets
    private static AssetBundle assets;
    private static Sprite climberSprite;
    private static Sprite hunterSprite;

    //General UI
    private static Image smallRoleIcon;
    //PassportUI
    private static GameObject roleUIElement;
    private static TextMeshProUGUI roleLabel;
    private static Button roleSwitcher;

    //Stores HunterData on Game Manager
    public class HunterDatabase : MonoBehaviourPun
    {
        public static HunterDatabase _;

        Dictionary<int, bool> isHunter = new Dictionary<int, bool>();

        private void Start()
        {
            if (_ != null)
            {
                Destroy(this);
                return;
            }
            _ = this;

            Log.LogDebug("Hunter Database attached to Game Handler");
        }

        public void ChangeRole(int actorNumber)
        {
            if (!isHunter.ContainsKey(actorNumber))
                isHunter[actorNumber] = false;
            isHunter[actorNumber] = !isHunter[actorNumber];
        }

        //Determine if is Hunter
        public bool isSetAsHunter(Character character)
        {
            if (isHunter.ContainsKey(character.view.Owner.ActorNumber))
                return isHunter[character.view.Owner.ActorNumber];
            return false;
        }
    }

    //Client to Server Updater
    public class HunterPlayerUpdater : MonoBehaviourPun
    {
        //Update on all clients
        [PunRPC]
        public void RPCA_ChangeRole(Character localCharacter)
        {
            HunterDatabase._.ChangeRole(localCharacter.view.Owner.ActorNumber);
        }
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

        //Load AssetBundle
        string sAssemblyLocation = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "assets");
        assets = AssetBundle.LoadFromFile(Path.Combine(sAssemblyLocation, "peak-hunter"));
        //Load Assets
        Texture2D climber_tex = assets.LoadAsset<Texture2D>("Climber_Icon");
        Texture2D hunter_tex = assets.LoadAsset<Texture2D>("Hunter_Icon");
        climberSprite = Sprite.Create(climber_tex, new Rect(0, 0, climber_tex.width, climber_tex.height), new Vector2(0.5f, 0.5f));
        hunterSprite = Sprite.Create(hunter_tex, new Rect(0, 0, hunter_tex.width, hunter_tex.height), new Vector2(0.5f, 0.5f));

        //Add Patches to Harmony
        Harmony.CreateAndPatchAll(typeof(Plugin));

        // Log our awake here so we can see it in LogOutput.log file
        Log.LogInfo($"Plugin {Name} is loaded!");
    }

    //---TESTING---
    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.G))
        {
            Vector3 spawnPosition = MapHandler.Instance.segments[MapHandler.Instance.currentSegment + 1].reconnectSpawnPos.position;
            switch ((Segment)MapHandler.Instance.currentSegment)
            {
                case Segment.TheKiln:
                    spawnPosition = MapHandler.Instance.respawnTheKiln.position;
                    break;
                case Segment.Peak:
                    spawnPosition = MapHandler.Instance.respawnThePeak.position;
                    break;
            }
            Character.localCharacter.WarpPlayer(spawnPosition, false);
        }

        if (Input.GetKeyDown(KeyCode.F))
        {
            if (HunterDatabase._.isSetAsHunter(Character.localCharacter))
            {
                Log.LogDebug("Applied Status to Hunter");
                Character.localCharacter.refs.afflictions.AddStatus(CharacterAfflictions.STATUSTYPE.Poison, 1);
            }
        }
    }

    //Add Database Code on Player
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

        smallRoleIcon.sprite = HunterDatabase._.isSetAsHunter(__instance) ? hunterSprite : climberSprite;
    }

    //Add Database Code on Player
    [HarmonyPatch(typeof(GameHandler), nameof(GameHandler.Awake))]
    [HarmonyPostfix]
    private static void AddHunterDataPatch(GameHandler __instance)
    {
        __instance.gameObject.AddComponent<HunterDatabase>();
    }

    //Setup Passport UI
    [HarmonyPatch(typeof(PassportManager), nameof(PassportManager.Initialize))]
    [HarmonyPostfix]
    private static void PassportUIPatch(PassportManager __instance)
    {
        bool localIsHunter = HunterDatabase._.isSetAsHunter(Character.localCharacter);

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
        roleLabel.text = !localIsHunter ? "CLIMBER" : "HUNTER";
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
        roleSwitcher.transform.Find("Box/Icon").GetComponent<RawImage>().texture = assets.LoadAsset<Texture2D>(!localIsHunter ? "Climber_Icon" : "Hunter_Icon");
        roleSwitcher.transform.Find("SFX Click").GetComponent<SFX_PlayOneShot>().sfxs =
            __instance.transform.Find("PassportUI/Canvas/Panel/Panel/BG/Options/Grid/UI_PassportGridButton/SFX Click").GetComponent<SFX_PlayOneShot>().sfxs;
        roleSwitcher.onClick = new Button.ButtonClickedEvent();
        roleSwitcher.onClick.AddListener(() => ChangeRole());

        //Done
        Log.LogDebug("Passport UI Loaded!");
    }

    //Boarding Pass UI Icons
    [HarmonyPatch(typeof(BoardingPass), nameof(BoardingPass.OnOpen))]
    [HarmonyPostfix]
    private static void BoardingPassUIPatch(BoardingPass __instance)
    {
        //Set corresponding sprites
        for (int i = 0; i < Character.AllCharacters.Count; i++)
            __instance.players[i].sprite = HunterDatabase._.isSetAsHunter(Character.AllCharacters[i]) ? hunterSprite : climberSprite;
    }

    //Click to Change Roles
    private static void ChangeRole()
    {
        //Change Role
        bool localIsHunter = !HunterDatabase._.isSetAsHunter(Character.localCharacter);
        Character.localCharacter.view.RPC("RPCA_ChangeRole", RpcTarget.All, Character.localCharacter);

        //Modify UI
        smallRoleIcon.sprite = localIsHunter ? hunterSprite : climberSprite;
        roleSwitcher.transform.Find("Box/Icon").GetComponent<RawImage>().texture = assets.LoadAsset<Texture2D>(!localIsHunter ? "Climber_Icon" : "Hunter_Icon");
        roleLabel.text = !localIsHunter ? "CLIMBER" : "HUNTER";
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
            if (!(allPlayerCharacter == null) && !allPlayerCharacter.photonView.Owner.IsInactive && !HunterDatabase._.isSetAsHunter(allPlayerCharacter))
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
    private static void ReachedNextStagePatch(Campfire __instance)
    {
        _.StartCoroutine(_.LoadNewStage());
    }

    //When first spawned and at each campfire
    public IEnumerator LoadNewStage()
    {
        if (HunterDatabase._.isSetAsHunter(Character.localCharacter))
        {
            yield return new WaitForSeconds(5);
            //Display The Hunter Is Near
            /*StartCoroutine(showHunterTitle());
            IEnumerator showHunterTitle()
            {
                yield return new WaitForSeconds(10);
                GUIManager.instance.SetHeroTitle("The Hunter Is Near", null);
            }*/

            //Hunter Cooldown
            onHunterCooldown = true;
            // Refresh except Curse
            Character.localCharacter.refs.afflictions.ClearAllStatus();
            // Give Poison til ready
            //WIP - You are able to heal if given enough time!
            Character.localCharacter.refs.afflictions.lastAddedStatus[(int)CharacterAfflictions.STATUSTYPE.Poison] = float.PositiveInfinity;
            Character.localCharacter.refs.afflictions.AddStatus(CharacterAfflictions.STATUSTYPE.Poison,
                Character.localCharacter.GetMaxStamina());

            // Wait
            float cooldownLength = initialCooldown + additionalCooldown * MapHandler.Instance.currentSegment;
            Log.LogDebug("Hunter Cooldown: " + cooldownLength);
            yield return new WaitForSeconds(cooldownLength);

            //Spawn Hunter
            onHunterCooldown = false;

            // Spawn Location
            Vector3 spawnPosition = MapHandler.Instance.segments[MapHandler.Instance.currentSegment].reconnectSpawnPos.position;
            switch ((Segment)MapHandler.Instance.currentSegment)
            {
                case Segment.TheKiln:
                    spawnPosition = MapHandler.Instance.respawnTheKiln.position;
                    break;
                case Segment.Peak:
                    spawnPosition = MapHandler.Instance.respawnThePeak.position;
                    break;
            }

            // Warp
            Character.localCharacter.photonView.RPC("WarpPlayerRPC", RpcTarget.All, spawnPosition, true);
            // Refresh except Curse again in case fell
            Character.localCharacter.refs.afflictions.ClearAllStatus();
            // Reset the Poison again
            Character.localCharacter.refs.afflictions.AddStatus(CharacterAfflictions.STATUSTYPE.Poison,
                Character.localCharacter.GetMaxStamina());
            // Set to heal immediately
            Character.localCharacter.refs.afflictions.lastAddedStatus[(int)CharacterAfflictions.STATUSTYPE.Poison] = 0;
            //WIP - DOESNT WORK
            Character.localCharacter.refs.afflictions.SubtractStatus(CharacterAfflictions.STATUSTYPE.Poison,
                Character.localCharacter.refs.afflictions.poisonReductionPerSecond * Time.deltaTime);
        }
    }

    [HarmonyPatch(typeof(Character), nameof(Character.HandlePassedOut))]
    [HarmonyPostfix]
    private static void HunterCantDieOnCooldownPatch(Character __instance)
    {
        if (onHunterCooldown && HunterDatabase._.isSetAsHunter(__instance))
            __instance.data.deathTimer = 0;
    }

    [HarmonyPatch(typeof(Character), nameof(Character.ClampStamina))]
    [HarmonyPostfix]
    private static void HunterExtraStaminaPatch(Character __instance)
    {
        if (HunterDatabase._.isSetAsHunter(__instance))
        {
            if (__instance.data.currentStamina == __instance.GetMaxStamina())
                __instance.SetExtraStamina(extraStamina);
        }
    }
}
