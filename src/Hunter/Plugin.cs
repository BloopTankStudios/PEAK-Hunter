using BepInEx;
using BepInEx.Logging;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using HarmonyLib;
using System.IO;
using System.Reflection;
using System.Collections.Generic;
using Photon.Pun;
using ExitGames.Client.Photon;

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

    //CharacterData

    //Assets
    private static AssetBundle? assets;

    //PassportUI
    private static GameObject? roleUIElement;
    private static TextMeshProUGUI? roleLabel;
    private static Button? roleSwitcher;

    //Handles determining if Player isHunter
    public class HunterNetworkManager : MonoBehaviourPunCallbacks
    {
        //Handlers
        public static HunterNetworkManager Instance;
        private static Dictionary<int, bool> storedIsHunter = new Dictionary<int, bool>();

        //Local variable
        public bool isHunter = false;

        //Setup Handlers
        private void Awake()
        {
            if ((Object)(object)Instance != null && (Object)(object)Instance != (Object)(object)this)
            {
                Destroy((Object)(object)gameObject);
                return;
            }
            Instance = this;
        }

        //Set info to players
        public void ChangeRole()
        {
            Hashtable customProperties = PhotonNetwork.LocalPlayer.CustomProperties;
            customProperties["Hunter_isHunter"] = !isHunter;
            PhotonNetwork.LocalPlayer.SetCustomProperties(customProperties);
        }

        //Update value for all
        public override void OnPlayerPropertiesUpdate(Photon.Realtime.Player targetPlayer, Hashtable changedProps)
        {
            isHunter = (bool)changedProps["Hunter_isHunter"];
            storedIsHunter[targetPlayer.ActorNumber] = isHunter;
        }

        //New Player added to list
        public override void OnJoinedRoom()
        {
            Photon.Realtime.Player[] playerList = PhotonNetwork.PlayerList;
            foreach (Photon.Realtime.Player val in playerList)
            {
                bool isPlayerHunter = false;
                if (val.CustomProperties.ContainsKey("Hunter_isHunter"))
                    isPlayerHunter = (bool)val.CustomProperties["Hunter_isHunter"];

                storedIsHunter[val.ActorNumber] = isPlayerHunter;
            }
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

        //Load AssetBundle
        string sAssemblyLocation = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "assets");
        assets = AssetBundle.LoadFromFile(Path.Combine(sAssemblyLocation, "peak-hunter"));

        //Add Patches to Harmony
        Harmony.CreateAndPatchAll(typeof(Plugin));

        // Log our awake here so we can see it in LogOutput.log file
        Log.LogInfo($"Plugin {Name} is loaded!");
    }

    //Add Network Handler to GameHandler
    [HarmonyPatch(typeof(GameHandler), nameof(GameHandler.Awake))]
    [HarmonyPostfix]
    private static void Postfix(GameHandler __instance)
    {
        if (!((Object)(object)HunterNetworkManager.Instance != (Object)null))
        {
            HunterNetworkManager.Instance = ((Component)__instance).gameObject.AddComponent<HunterNetworkManager>();
        }
    }

    //Setup UI
    [HarmonyPatch(typeof(PassportManager), nameof(PassportManager.Awake))]
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
        roleLabel.GetComponent<RectTransform>().anchorMin = new Vector2(0.5f, 0);
        roleLabel.GetComponent<RectTransform>().anchorMax = new Vector2(0.5f, 1);
        roleLabel.GetComponent<RectTransform>().sizeDelta = new Vector2(160, 0);
        //Modify Text
        roleLabel.text = "CLIMBER";
        roleLabel.fontSize = 29;
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
        roleSwitcher.transform.Find("Box/Icon").GetComponent<RawImage>().texture = assets.LoadAsset<Texture2D>("Climber_Icon");
        roleSwitcher.transform.Find("SFX Click").GetComponent<SFX_PlayOneShot>().sfxs =
            __instance.transform.Find("PassportUI/Canvas/Panel/Panel/BG/Options/Grid/UI_PassportGridButton/SFX Click").GetComponent<SFX_PlayOneShot>().sfxs;
        roleSwitcher.onClick = new Button.ButtonClickedEvent();
        roleSwitcher.onClick.AddListener(() => ChangeRole());

        //Done
        Log.LogDebug("Passport UI Loaded!");
    }

    private static void ChangeRole()
    {
        //Change Role
        bool localIsHunter = !HunterNetworkManager.Instance.isHunter;
        HunterNetworkManager.Instance.ChangeRole();
        //Character.localCharacter.view.RPC("RPCA_ChangeRole", RpcTarget.All, Character.localCharacter);

        //Modify UI
        roleSwitcher.transform.Find("Box/Icon").GetComponent<RawImage>().texture = assets.LoadAsset<Texture2D>(!localIsHunter ? "Climber_Icon" : "Hunter_Icon");
        roleLabel.text = !localIsHunter ? "CLIMBER" : "HUNTER";

        //Done
        Log.LogDebug("Role Changed");
    }

    //Update on all clients
    /*[PunRPC]
    public static void RPCA_ChangeRole(Character localCharacter)
    {
        isHunter[localCharacter] = !isHunter[localCharacter];
        localCharacter.DieInstantly();
    }*/
}
