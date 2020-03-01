﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace CombatExtended
{
	public class JobGiver_CheckReload : ThinkNode_JobGiver
	{
		#region Fields
		
		// lower priority = less likely to be done.  Need to give this a priority below JobGiver_Update Loadout's high priority setting.
		const float reloadPriority = 9.1f; // in JobGiver_UpdateLoadout, 9.2 is high priority, 3 is lower priority.
		
		#endregion Fields
		
		#region ThinkNode_JobGiver
		
		/// <summary>
		/// Informs rimworld how important this job is relative to other potential jobs for the pawn.
		/// </summary>
		/// <param name="pawn">Pawn that this job is to be considered.</param>
		/// <returns>float value indicating importance of the job.</returns>
		public override float GetPriority(Pawn pawn)
		{
			// need to generate the job here which involves replicating some code from TryStartReload...
			
			// this MUST not even try to do anything if the inventory state isn't in a good state.
			// There are 3 states that JobGiver_UpdateLoadout can end in and I need to know which of those 3 states are active.
			
			// would be nice to communicate with JobGiver_UpdateLoadout but it will suffice to set our priority below theirs.
			
			if (!pawn.Drafted && DoReloadCheck(pawn, out var _, out var __))
				return reloadPriority;
            
			return 0.0f;
		}
		
		/// <summary>
		/// If RimWorld decides the pawn should do this job, this is called to get the pawn working on it.
		/// </summary>
		/// <param name="pawn">Pawn that the job is supposed to take place on.</param>
		/// <returns>Job that the pawn is to be working on.</returns>
		protected override Job TryGiveJob(Pawn pawn)
		{
			Job reloadJob = null;

            // we relied on DoReloadCheck() to do error checking of many variables.
            if (DoReloadCheck(pawn, out var comp, out var link)
                && comp.PreReload(link))    //sets link
			{
	            // Get the reload job from the comp.
	            reloadJob = comp.TryMakeReloadJob();
			}
			return reloadJob;
		}
		
		#endregion
		
		#region Methods
		
		/* Rough Algorithm
		 * Need Things so the collection of ammo users that use magazines.  Also need a collection of ammo (ThingDef is OK here).
		 * For each weapon (that fits above),
		 * -If we have no ammo in inventory that the gun is loaded with, check loadouts/holdtracker for a clip's worth of ammo that the gun contains.
		 * --Find ammo the gun uses that we have a clip's worth in inventory (should check it against loadout/holdtracker as well)
		 * -If weapon is low on ammo and we have enough in inventory to fill it up.
		 * 
		 * If either of the above are true, trigger a reload.
		 */
		/// <summary>
		/// Check's the pawn's equipment and inventory for weapons that could use a reload.
		/// </summary>
		/// <param name="pawn">Pawn to check the equipment and inventory of.</param>
		/// <param name="reloadWeapon">Thing weapon which needs to be reloaded.</param>
		/// <param name="reloadAmmo">AmmoDef ammo to reload the gun with.</param>
		/// <returns>Bool value indicating if the job needs to be done.</returns>
		private bool DoReloadCheck(Pawn pawn, out CompAmmoUser reloadUser, out AmmoLink reloadLink)
		{
            reloadUser = null;
			reloadLink = null;
			
			// First need to create the collections that will be searched.
			List<ThingWithComps> guns = new List<ThingWithComps>();
			CompInventory inventory = pawn.TryGetComp<CompInventory>();
			CompAmmoUser tmpComp;
            Loadout loadout = pawn.GetLoadout();
            bool pawnHasLoadout = loadout != null && !loadout.Slots.NullOrEmpty();

            #region Checks
            if (inventory == null)
				return false; // There isn't any work to do since the pawn doesn't have a CE Inventory.
			
			guns.Add(pawn.equipment.Primary);

            // CompInventory doesn't track equipment and it's desired to check the pawn's equipped weapon before inventory items so need to copy stuff from Inventory Cache.
            guns.AddRange(inventory.rangedWeaponList);
			
			if (guns.NullOrEmpty())
				return false; 
            #endregion

            // look at each gun...
            foreach (ThingWithComps gun in guns)
			{
				// Get key stats of the weapon.
				tmpComp = gun?.TryGetComp<CompAmmoUser>();

                // Gun doesn't use ammo
                if (tmpComp == null || !tmpComp.HasMagazine)
                    continue;

			  //AmmoDef ammoType = tmpComp.CurrentAmmo;
                
				int deficitSize = Math.Min(tmpComp.Props.magazineSize, tmpComp.Props.magazineSize - tmpComp.CurMagCount);

                // Is the gun NOT loaded with ammo in a Loadout/HoldTracker?
                if (tmpComp.UseAmmo && pawnHasLoadout
                    && tmpComp.adders.All(x => !TrackingSatisfied(pawn, x.def, tmpComp.CurrentLink.AmountToLoadMagazine(x, tmpComp, true))))
				{
                    foreach (var thing in inventory.ammoList)
                    {
                        var maxCharge = tmpComp.Props.ammoSet.MaxCharge(thing.def);

                        if (maxCharge <= 0)
                            continue;

                        //var toFill = tmpComp.CurrentLink.AmountToConsume(thing, tmpComp, true);

                        var toFill = (float)deficitSize / (float)maxCharge;

                        if (toFill > 0 && TrackingSatisfied(pawn, thing.def, Mathf.CeilToInt(toFill))
                            && toFill < inventory.AmmoCountOfDef(thing.def as AmmoDef))
                        {
                            reloadUser = tmpComp;
                            reloadLink = tmpComp.Props.ammoSet.Containing(new ThingDefCountClass(thing.def, maxCharge));
                            return true;
                        }
                    }
				}
				
				// Is the gun low on ammo? -- CurMagCount appropriate because it concerns loaded ammo
				if (tmpComp.CurMagCount < tmpComp.Props.magazineSize)
                {
                    // Is the gun loaded with ammo other than PlayerForced ammo, and is that ammo available?
                    if (!tmpComp.UseAmmo)
                    {
                        reloadUser = tmpComp;
                        reloadLink = tmpComp.SelectedLink;
                        return true;
                    }
                    else if (tmpComp.LinksMatch)
                    {
                        // Do we have enough ammo in the inventory to top it off?
                        var countCharges = 0;
                        foreach (var defCount in tmpComp.CurrentLink.adders)
                        {
                            if (defCount.count < deficitSize)
                                countCharges += defCount.count * inventory.AmmoCountOfDef(defCount.thingDef as AmmoDef);

                            if (countCharges >= deficitSize)
                            {
                                reloadUser = tmpComp;
                                reloadLink = tmpComp.CurrentLink;
                                return true;
                            }
                        }
                    }
                    
					//} else {  // (ProfoundDarkness) I think the idea of this branch was that the JobGiver might initiate fetching ammo but it actually runs AFTER the one that fetches ammo for loadout.
						// There wasn't enough in the inventory to top it off.  At this point we know the loadout is satisfied for this ammo...
						// We could do a more strict check to see if the pawn's loadout is satisfied to pick up ammo and if not swap to another ammo...?
				}
			}
			return false;
		}
		
		/// <summary>
		/// Basically counts the amount of ThingDef that a loadout can have.  Assumes Pawn has a Loadout (caller checked this already).
		/// </summary>
		/// <param name="pawn">Pawn who's loadout and holdtracker is to be inspected.</param>
		/// <param name = "def">ThingDef to match</param>
		/// <param name = "amount">int amount of ThingDef desired to be covered by Loadout/HoldTracker</param>
		/// <returns>int amount of that def that can be had.</returns>
		private bool TrackingSatisfied(Pawn pawn, ThingDef def, int amount)
		{
			// first check loadouts...
			Loadout loadout = pawn.GetLoadout();
			
			foreach (LoadoutSlot slot in loadout.Slots)
			{
				if (slot.thingDef != null)
				{
					if (slot.thingDef == def)
						amount -= slot.count;
				} else if (slot.genericDef != null)
				{
					if (slot.genericDef.lambda(def))
						amount -= slot.count;
				}
				if (amount <= 0)
					return true;
			}
			
			// if we got here, also check holdRecords.
			List<HoldRecord> records = pawn.GetHoldRecords();
			
			if (!records.NullOrEmpty())
			{
				foreach (HoldRecord rec in records)
				{
					if (rec.thingDef == def)
						amount -= rec.count;
				}
			}
			
			return false;
		}
		
		#endregion Methods
	}
}