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
        
        // Because hunger might get depleted before (or after) geting awaken, we'll need to calculate statuses as if the hunger didn't got away
        var additionnalHunger = 0f;

        foreach (var hung in item.gameObject.GetComponents<Action_RestoreHunger>())
        {
            additionnalHunger += hung.restorationAmount;
        }
        
        var havePassedOut = (character.refs.afflictions.statusSum + additionnalHunger) > 0.9900000095367432;
        
        if (character.data.passedOut || havePassedOut)
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

                    if (vToCompare <= 0f)
                    {
                        Plugin.Log.LogDebug(
                            $"Status {(CharacterAfflictions.STATUSTYPE) i} is not on {character.characterName}. Skipping...");
                        continue;
                    }

                    if ((CharacterAfflictions.STATUSTYPE) i == CharacterAfflictions.STATUSTYPE.Petrify)
                    {
                        Plugin.Log.LogDebug(
                            $"Can't wake up from Status {(CharacterAfflictions.STATUSTYPE) i}. Skipping...");
                        continue;
                    }
                    
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

                if (largestStatusIndex == -1)
                {
                    Plugin.Log.LogWarning($"No largest status effect were found, yet {character.characterName} is STILL passed out. Something is wrong!");
                    break;
                }

                var status = (CharacterAfflictions.STATUSTYPE) largestStatusIndex;

                Plugin.Log.LogDebug($"Biggest status: {status} @ {largestStatusValue}");

                switch (status)
                {
                    case CharacterAfflictions.STATUSTYPE.Weight:
                    {
                        if (character.data.IsCarryingCharacter)
                        {
                            if (character.data.fullyPassedOut)
                            {
                                Plugin.Log.LogWarning(
                                    $"{character.characterName} is FULLY passed out and carrying {character.data.carriedPlayer}, which should NOT happen! Dropping {character.data.carriedPlayer} anyway...");
                            }
                            
                            character.DropCarriedCharacter(false); 
                        }

                        Plugin.Log.LogWarning(
                            $"Weight is the biggest status. Dropping all items from {character.characterName}...");
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
                                Plugin.Log.LogInfo($"Removing {physicalThorn} from {character.characterName}...");
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
                                Plugin.Log.LogInfo($"Removing {physicalThorn} from {character.characterName}...");
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

                        Plugin.Log.LogInfo($"Removing {status} from {character.characterName}...");
                        character.refs.afflictions.SubtractStatus(status, Mathf.Abs(FULL_HEAL));
                        
                        // Remove bugfix
                        foreach (var allAttachedBug in Bugfix.AllAttachedBugs)
                        {
                            if (allAttachedBug.Value == character)
                            {
                                allAttachedBug.Key.Interact(character);
                                break;
                            }
                        }

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

                        Plugin.Log.LogInfo($"Removing {status} from {character.characterName}...");
                        character.refs.afflictions.SubtractStatus(status, Mathf.Abs(FULL_HEAL));

                        break;
                    }

                    default:
                    {
                        Plugin.Log.LogInfo($"Removing {status} from {character.characterName}...");
                        character.refs.afflictions.SubtractStatus(status, Mathf.Abs(FULL_HEAL));
                        break;
                    }
                }
            }
        }
        else
        {
            Plugin.Log.LogInfo($"Burning up {character.characterName}...");
            // Gives energy (with heat tho)
            character.AddExtraStamina(m_extraStamAmountIfAwake);
            character.refs.afflictions.AddStatus(CharacterAfflictions.STATUSTYPE.Hot, Mathf.Abs(FULL_BAR));
            
            // character.refs.afflictions.AddAffliction(m_toGiveIfAwake);
        }
        
        // Remove any drowsiness.
        character.refs.afflictions.SubtractStatus(CharacterAfflictions.STATUSTYPE.Drowsy, Mathf.Abs(FULL_HEAL));

        // Acheivements handleing...
        if (isPassedOut && character.refs.afflictions.statusSum <= 1 && hasFeeder)
        {
            GameUtils.instance.ThrowEmergencyPreparednessAchievement(feeder.photonView.Owner);
        }
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


                Plugin.Log.LogInfo($"Destroying {stuckItem} from {character.characterName}...");
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