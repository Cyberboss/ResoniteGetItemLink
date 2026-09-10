using System;
using System.Reflection;

using Elements.Core;

using FrooxEngine;
using FrooxEngine.Store;
using FrooxEngine.UIX;

using HarmonyLib;

using ResoniteModLoader;

namespace GetItemLink
{
	public sealed class GetItemLink : ResoniteMod
	{
		// UPDATE VERSIONS HERE AND IN GITHUB ACTIONS. DON'T FORGET RELEASE NOTES!
		internal const string VersionConstant = "2.0.0";

		const string ButtonsRootName = "GetItemLink Buttons";
		const string GetAssetTag = "Get Asset URI";
		const string GetRecordTag = "Get Record URI";
		const string EditRecordTag = "Edit Record";

		const InventoryBrowser.SpecialItemType UniqueSpecialItemType = (InventoryBrowser.SpecialItemType)(-1);// doing this so the buttons show up on component init

		static ModConfiguration? Config;

		static FieldInfo? ItemFieldInfo;

		static FieldInfo? DirectoryFieldInfo;

		/// <inheritdoc />
		public override string Name => nameof(GetItemLink);

		/// <inheritdoc />
		public override string Author => "EIA485 & Dominion";

		/// <inheritdoc />
		public override string Version => VersionConstant;

		/// <inheritdoc />
		public override string? Link => "https://github.com/Cyberboss/ResoniteGetItemLink";

		[AutoRegisterConfigKey]
		private static readonly ModConfigurationKey<bool> Enabled = new ModConfigurationKey<bool>("Enabled", "Mod Enabled. Requires Resonite restart.", () => true);

		public override void OnEngineInit()
		{
			Config = GetConfiguration()!;
			FieldInfo? GetInventoryItemUIField(string fieldName)
			{
				var result = typeof(InventoryItemUI).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
				if (result == null)
				{
					Error($"Unable to load required field: {nameof(InventoryItemUI)}.{fieldName}");
				}

				return result;
			}

			ItemFieldInfo = GetInventoryItemUIField("Item");
			DirectoryFieldInfo = GetInventoryItemUIField("Directory");

			if (ItemFieldInfo == null || DirectoryFieldInfo == null)
			{
				Enabled.Value = false;
			}

			Config.Save(true);
			if (Enabled.Value)
			{
				var harmony = new Harmony("net.dextraspace.GetItemLink");
				harmony.PatchAll();
			}
		}

		/// <summary>
		/// Patches for <see cref="InventoryBrowser"/>.
		/// </summary>
		[HarmonyPatch(typeof(InventoryBrowser))]
		class GetItemLinkPatch
		{
			[HarmonyPrefix]
			[HarmonyPatch("OnItemSelected")]
			public static void OnItemSelectedPrefix(ref InventoryBrowser.SpecialItemType __state, Sync<InventoryBrowser.SpecialItemType> ____lastSpecialItemType)
			{
				__state = ____lastSpecialItemType.Value;
			}

			[HarmonyPostfix]
			[HarmonyPatch("OnItemSelected")]
			public static void OnItemSelectedPostfix(InventoryBrowser __instance, BrowserItem currentItem, InventoryBrowser.SpecialItemType __state, SyncRef<Slot> ____buttonsRoot)
			{
				if (__instance.World != Userspace.UserspaceWorld)
					return;

				if (!(currentItem is InventoryItemUI inventoryItemUi && __state != InventoryBrowser.ClassifyItem(inventoryItemUi) || __state == UniqueSpecialItemType))
					return;

				Slot buttonRoot = ____buttonsRoot.Target[0];
				UIBuilder ui = new(buttonRoot);
				RadiantUI_Constants.SetupDefaultStyle(ui);
				var hori = ui.HorizontalLayout(4);
				hori.Slot.Name = ButtonsRootName;

				// Weird workaround to force UIX reflow, otherwise buttons are invisible
				hori.PaddingLeft.Value = 1;
				__instance.RunInUpdates(
					0,
					() =>
					{
						hori.PaddingLeft.Value = 0;
					});

				AddButton(
					(IButton button, ButtonEventData eventData) => ItemLink(button, __instance.SelectedInventoryItem, false),
					GetAssetTag, colorX.Purple, OfficialAssets.Graphics.Badges.Cheese,
					ui
				);

				AddButton(
					(IButton button, ButtonEventData eventData) => ItemLink(button, __instance.SelectedInventoryItem, true),
					GetRecordTag, colorX.Brown, OfficialAssets.Graphics.Badges.potato,
					ui
				);

				AddButton(
					(IButton button, ButtonEventData eventData) =>
					{
						RecordEditForm editForm;
						var overlayMngr = __instance.Slot.GetComponentInParents<ModalOverlayManager>();
						if (overlayMngr == null)
						{
							var slot = __instance.LocalUserSpace.AddSlot("Record Edit Form");
							slot.PositionInFrontOfUser(float3.Backward, float3.Right * 0.5f);
							editForm = RecordEditForm.OpenDialogWindow(slot);
						}
						else
						{
							editForm = overlayMngr.OpenModalOverlay(new float2(.25f, .8f), "Edit Record").Slot.AttachComponent<RecordEditForm>();
						}
						var r = GetRecord(__instance.SelectedInventoryItem);
						if (r == null) return;
						AccessTools.Method(typeof(RecordEditForm), "Setup").Invoke(editForm, new[] { null, r });
					},
					EditRecordTag, colorX.Orange, OfficialAssets.Graphics.Icons.Dash.Settings,
					ui
				);
			}

			[HarmonyPrefix]
			[HarmonyPatch("OnChanges")]
			public static void OnChangesPrefix(InventoryBrowser __instance, SyncRef<Slot> ____buttonsRoot)
			{
				if (__instance.World != Userspace.UserspaceWorld)
					return;

				Slot buttonRoot = ____buttonsRoot.Target[0];
				bool enableButtons = __instance.SelectedInventoryItem != null;
				Slot buttons = buttonRoot.FindChild(ButtonsRootName);
				if (buttons != null)
				{
					foreach (var child in buttons.Children)
					{
						if (child.Tag == GetAssetTag)
						{
							child.GetComponent<Button>().Enabled = enableButtons && (GetLink(__instance.SelectedInventoryItem, false) != null);
							child[0].GetComponent<Image>().Tint.Value = colorX.Black;
						}
						else if (child.Tag == GetRecordTag)
						{
							child.GetComponent<Button>().Enabled = enableButtons && (GetLink(__instance.SelectedInventoryItem, true) != null);
							child[0].GetComponent<Image>().Tint.Value = colorX.Black;
						}
						else if (child.Tag == EditRecordTag)
						{
							child.GetComponent<Button>().Enabled = enableButtons && GetRecord(__instance.SelectedInventoryItem) != null;
							child[0].GetComponent<Image>().Tint.Value = colorX.Black;
						}
					}
				}
			}

			[HarmonyPostfix]
			[HarmonyPatch("OnAwake")]
			public static void InitializeSyncMembersPostfix(InventoryBrowser __instance, Sync<InventoryBrowser.SpecialItemType> ____lastSpecialItemType)
			{
				if (__instance.World == Userspace.UserspaceWorld)
				{
					____lastSpecialItemType.Value = UniqueSpecialItemType;
				}
			}
		}

		public static void AddButton(ButtonEventHandler onPress, string tag, colorX tint, Uri sprite, UIBuilder ui)
		{
			var userButton = ui.Button(sprite, tint);
			var buttonSlot = userButton.Slot;
			buttonSlot.Tag = tag;
			userButton.LocalPressed += onPress;
			userButton.ColorDrivers.RemoveAt(userButton.ColorDrivers.Count - 1);
			buttonSlot[0].GetComponent<Image>().Tint.Value = colorX.Black;

			// https://github.com/Psychpsyo/Tooltippery Support, implemented based on the readme
			buttonSlot.AttachComponent<Comment>().Text.Value = "TooltipperyLabel:" + tag;
		}

		public static void ItemLink(IButton button, InventoryItemUI Item, bool type)
		{
			var link = GetLink(Item, type);
			if (link != null)
			{
				var clipboard = Engine.Current.InputInterface.Clipboard;
				if (clipboard == null)
				{
					Error("Clipboard was null!");
				}
				else
				{
					clipboard.SetText(link);
					button.Slot[0].GetComponent<Image>().Tint.Value = colorX.White;
				}
			}
			else
			{
				button.Slot[0].GetComponent<Image>().Tint.Value = colorX.Red;
			}
		}

		static Record? GetRecord(InventoryItemUI? item)
			=> (Record?)ItemFieldInfo!.GetValue(item) ?? ((RecordDirectory?)DirectoryFieldInfo!.GetValue(item))?.EntryRecord;

		static string? GetLink(InventoryItemUI? item, bool type)
		{
			if (item == null)
			{
				return null;
			}

			var record = GetRecord(item);
			return type ? record?.GetUrl(Engine.Current.PlatformProfile).ToString() : record?.AssetURI;
		}
	}
}
