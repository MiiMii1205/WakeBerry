using System.Collections;
using Peak.Afflictions;
using Photon.Pun;
using UnityEngine;
using Zorro.Core;

namespace WakeBerry.Behaviours;

public class Action_WakeUp : ItemAction
{
    public float m_extraStamAmountIfAwake = 0.35f;

    public const float FULL_HEAL = 5f;
    public const float FULL_BAR = 1f;

    public Affliction m_toGiveIfAwake;

    public override void RunAction()
    {
        var isPassedOut = character.data.passedOut;
        var hasFeeder = item.TryGetFeeder(out var feeder);

        character.refs.afflictions.SubtractStatus(CharacterAfflictions.STATUSTYPE.Drowsy, Mathf.Abs(FULL_HEAL));

        if (character.data.passedOut)
        {
            Plugin.Log.LogInfo($"Waking up {character.characterName}...");

            while (character.refs.afflictions.shouldPassOut)
            {
                int largestStatusIndex = -1;
                float largestStatusValue = float.NegativeInfinity;

                // Get the largest status 
                for (int i = 0, length = character.refs.afflictions.currentStatuses.Length; i < length; ++i)
                {
                    var vToCompare = character.refs.afflictions.currentStatuses[i];

                    switch ((CharacterAfflictions.STATUSTYPE) i)
                    {
                        case CharacterAfflictions.STATUSTYPE.Weight:
                            // Weight should be the last resort only.
                            vToCompare *= 0.00001f;
                            break;
                        case CharacterAfflictions.STATUSTYPE.Petrify:
                            // Can't wake up by removing petrification...
                            vToCompare = float.NegativeInfinity;
                            break;
                    }

                    if (vToCompare > largestStatusValue)
                    {
                        largestStatusIndex = i;
                        largestStatusValue = vToCompare;
                    }
                }

                var status = (CharacterAfflictions.STATUSTYPE) largestStatusIndex;

                Plugin.Log.LogDebug($"Biggest status: {status} @ {largestStatusValue}");

                switch (status)
                {
                    case CharacterAfflictions.STATUSTYPE.Weight:
                    {
                        Plugin.Log.LogWarning(
                            $"Weight is the biggest status, which should NOT happen! Dropping all items anyways...");
                        if (character.data.IsCarryingCharacter)
                        {
                            character.DropCarriedCharacter(false);
                        }

                        // Drop everything you have RIGHT NOW!
                        character.refs.items.DropAllItems(true);
                        break;
                    }

                    case CharacterAfflictions.STATUSTYPE.Arrow:
                    {
                        // Remove all arrows
                        foreach (var physicalThorn in character.refs.afflictions.physicalThorns)
                        {
                            if (physicalThorn.isArrow)
                            {
                                character.refs.afflictions.RemoveThorn(physicalThorn, false);
                            }
                        }

                        // Just make sure every arrow statues are gone.
                        character.refs.afflictions.SubtractStatus(status, Mathf.Abs(FULL_HEAL));

                        break;
                    }
                    case CharacterAfflictions.STATUSTYPE.Thorns:
                    {
                        // Remove all cactus thorns
                        foreach (var physicalThorn in character.refs.afflictions.physicalThorns)
                        {
                            if (physicalThorn.isThorn)
                            {
                                character.refs.afflictions.RemoveThorn(physicalThorn, false);
                            }
                        }

                        // Remove sticky cactus balls 
                        foreach (var stuckItem in StickyItemComponent.ALL_STUCK_ITEMS)
                        {
                            // Ownership transfers might take a bit of time...
                            StartCoroutine(DestroyStuckItems(stuckItem));
                        }

                        // Just make sure every thorn statues are gone.
                        character.refs.afflictions.SubtractStatus(status, Mathf.Abs(FULL_HEAL));

                        break;
                    }

                    case CharacterAfflictions.STATUSTYPE.Poison:
                    {
                        // Acheivements handleing...
                        character.refs.afflictions.ClearPoisonAfflictions();

                        var amount = Mathf.RoundToInt(
                            Mathf.Min(
                                character.refs.afflictions.GetCurrentStatus(CharacterAfflictions.STATUSTYPE
                                    .Poison), Mathf.Abs(FULL_HEAL)) * 100f);

                        if (hasFeeder)
                        {
                            GameUtils.instance.IncrementFriendPoisonHealing(amount, feeder.photonView.Owner);
                        }
                        else
                        {
                            Singleton<AchievementManager>.Instance.IncrementSteamStat(STEAMSTATTYPE.PoisonHealed,
                                amount);
                        }

                        character.refs.afflictions.SubtractStatus(status, Mathf.Abs(FULL_HEAL));

                        break;
                    }

                    case CharacterAfflictions.STATUSTYPE.Injury:
                    {
                        // Acheivements handleing...
                        if (hasFeeder)
                        {
                            var amt = Mathf.RoundToInt(
                                Mathf.Min(
                                    this.character.refs.afflictions.GetCurrentStatus(CharacterAfflictions.STATUSTYPE
                                        .Injury), Mathf.Abs(FULL_HEAL)) * 100f);

                            GameUtils.instance.IncrementFriendHealing(amt, feeder.photonView.Owner);
                        }

                        character.refs.afflictions.SubtractStatus(status, Mathf.Abs(FULL_HEAL));

                        break;
                    }

                    default:
                    {
                        character.refs.afflictions.SubtractStatus(status, Mathf.Abs(FULL_HEAL));
                        break;
                    }
                }
            }
        }
        else
        {
            // Gives energy (with heat tho)
            character.AddExtraStamina(m_extraStamAmountIfAwake);
            character.refs.afflictions.AddStatus(CharacterAfflictions.STATUSTYPE.Hot, Mathf.Abs(FULL_BAR));
            
            StartCoroutine(DelayedSpeed());
            
            // character.refs.afflictions.AddAffliction(m_toGiveIfAwake);
        }

        // Acheivements handleing...
        if (isPassedOut && character.refs.afflictions.statusSum <= 1 && hasFeeder)
        {
            GameUtils.instance.ThrowEmergencyPreparednessAchievement(feeder.photonView.Owner);
        }
    }

    private IEnumerator DelayedSpeed()
    {
        // Wait until all the heat given to the player when eaten gets removed.
        // It might not be enough but at this point, skill issues.
        var delay = (FULL_BAR * 1f) / character.refs.afflictions.hotReductionPerSecond;
        yield return new WaitForSeconds(character.refs.afflictions.hotReductionCooldown + delay);
        character.refs.afflictions.AddAffliction(m_toGiveIfAwake);
    }

    private IEnumerator DestroyStuckItems(StickyItemComponent stuckItem)
    {
        if (stuckItem.stuckToCharacter == character && stuckItem is CactusBall)
        {
            if (stuckItem.photonView.OwnershipTransfer != OwnershipOption.Fixed)
            {
                if (!stuckItem.photonView.IsMine)
                {
                    // Requesting ownership for destructions 
                    stuckItem.photonView.RequestOwnership();

                    yield return new WaitUntil(() => stuckItem.photonView.IsMine);
                }

                // Just destroy the cactus ball. We don't want to have drop down and get stuck again
                PhotonNetwork.Destroy(stuckItem.gameObject);
            }
            else
            {
                Plugin.Log.LogWarning(
                    $"Can't gain ownership of {stuckItem.name}. Ownership transfer is set to {stuckItem.photonView.OwnershipTransfer}");
            }
        }
    }
}