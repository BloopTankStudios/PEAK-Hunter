using Hunter;
using UnityEngine;

public class CampfireRadiusTrigger : MonoBehaviour
{
    public bool playerIsWithinBounds = false;

    //Only apply physics to LocalPlayer
    void OnCollisionEnter(Collision collision)
    {
        if (!(CharacterRagdoll.TryGetCharacterFromCollider(collision.collider, out var player) && player.IsLocal))
        {
            // Ignore the collision if it's not the specific Rigidbody
            Physics.IgnoreCollision(collision.collider, GetComponent<Collider>(), true);
        }
    }

    //Enter Safe Zone
    private void OnTriggerEnter(Collider other)
    {
        if (playerIsWithinBounds)
            return;

        if (CharacterRagdoll.TryGetCharacterFromCollider(other, out var player) && player.IsLocal)
        {
            playerIsWithinBounds = true;
            Plugin.Log.LogDebug("Local Player within Campfire");
            StartCoroutine(Plugin._.showMessage("SAFE ZONE REACHED", true));
        }
    }

    //Exit Safe Zone
    private void OnTriggerExit(Collider other)
    {
        if (!playerIsWithinBounds)
            return;

        if (CharacterRagdoll.TryGetCharacterFromCollider(other, out var player) && player.IsLocal)
        {
            playerIsWithinBounds = false;
            Plugin.Log.LogDebug("Local Player outside Campfire");
            StartCoroutine(Plugin._.showMessage("LEAVING SAFE ZONE", true));
        }  
    }
}