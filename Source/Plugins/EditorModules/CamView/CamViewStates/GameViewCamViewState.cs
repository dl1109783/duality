﻿using System;
using System.Linq;
using System.Xml;
using System.Xml.Linq;
using System.Collections.Generic;
using System.Windows.Forms;
using System.Drawing;

using AdamsLair.WinForms.ItemModels;
using AdamsLair.WinForms.ItemViews;

using Duality;
using Duality.Components;
using Duality.Resources;
using Duality.Drawing;

using Duality.Editor.Controls.ToolStrip;
using Duality.Editor.Plugins.CamView.Properties;


namespace Duality.Editor.Plugins.CamView.CamViewStates
{
	/// <summary>
	/// Provides a full preview of the game within the editor. 
	/// This state renders the games actual audiovisual output and reroutes user input to the game.
	/// </summary>
	public class GameViewCamViewState : CamViewState
	{
		private enum SpecialRenderSize
		{
			Fixed,
			CamView,
			GameTarget
		}

		private List<ToolStripItem> toolbarItems = new List<ToolStripItem>();
		private ToolStripTextBoxAdv textBoxRenderWidth = null;
		private ToolStripTextBoxAdv textBoxRenderHeight = null;
		private ToolStripDropDownButton dropdownResolution = null;
		private MenuModel resolutionMenuModel = new MenuModel();
		private MenuStripMenuView resolutionMenuView = null;

		private Point2 targetRenderSize = Point2.Zero;
		private SpecialRenderSize targetRenderSizeMode = SpecialRenderSize.CamView;
		private List<Point2> recentTargetRenderSizes = new List<Point2>();
		private bool isUpdatingUI = false;
		private RenderTarget outputTarget = null;
		private Texture outputTexture = null;
		private DrawDevice blitDevice = null;


		public override string StateName
		{
			get { return Properties.CamViewRes.CamViewState_GameView_Name; }
		}
		public override Rect RenderedViewport
		{
			get { return this.LocalGameWindowRect; }
		}
		public override Point2 RenderedImageSize
		{
			get { return this.TargetRenderSize; }
		}
		private Point2 GameTargetSize
		{
			get
			{
				bool isUsingForcedSize = 
					DualityApp.AppData.ForcedRenderSize.X != 0 && 
					DualityApp.AppData.ForcedRenderSize.Y != 0;
				return isUsingForcedSize ? 
					DualityApp.AppData.ForcedRenderSize : 
					DualityApp.UserData.WindowSize;
			}
		}
		private Point2 CamViewTargetSize
		{
			get
			{
				return new Point2(
					this.RenderableControl.ClientSize.Width,
					this.RenderableControl.ClientSize.Height);
			}
		}
		private Point2 TargetRenderSize
		{
			get { return this.targetRenderSize; }
			set
			{
				if (this.targetRenderSize != value)
				{
					this.targetRenderSize = value;
					this.UpdateTargetRenderSizeUI();
					this.Invalidate();
				}
			}
		}
		private SpecialRenderSize TargetRenderSizeMode
		{
			get { return this.targetRenderSizeMode; }
			set
			{
				this.targetRenderSizeMode = value;
				this.ApplyTargetRenderSizeMode();
			}
		}
		private bool TargetSizeFitsClientArea
		{
			get
			{
				return 
					this.targetRenderSize.X <= this.RenderableControl.ClientSize.Width &&
					this.targetRenderSize.Y <= this.RenderableControl.ClientSize.Height;
			}
		}
		private bool UseOffscreenBuffer
		{
			get { return !this.TargetSizeFitsClientArea; }
		}
		private Rect LocalGameWindowRect
		{
			get
			{
				TargetResize resizeMode = this.TargetSizeFitsClientArea ? 
					TargetResize.None : 
					TargetResize.Fit;
				
				Vector2 clientSize = new Vector2(this.ClientSize.Width, this.ClientSize.Height);
				Vector2 localWindowSize = resizeMode.Apply(this.TargetRenderSize, clientSize);

				return Rect.Align(
					Alignment.Center,
					clientSize.X * 0.5f,
					clientSize.Y * 0.5f,
					localWindowSize.X,
					localWindowSize.Y);
			}
		}


		public GameViewCamViewState()
		{
			this.CameraActionAllowed = false;
			this.EngineUserInput = true;
		}

		private void AddToolbarItems()
		{
			this.textBoxRenderWidth = new ToolStripTextBoxAdv("textBoxRenderWidth");
			this.textBoxRenderWidth.BackColor = Color.FromArgb(196, 196, 196);
			this.textBoxRenderWidth.AutoSize = false;
			this.textBoxRenderWidth.Width = 35;
			this.textBoxRenderWidth.MaxLength = 4;
			this.textBoxRenderWidth.AcceptsOnlyNumbers = true;
			this.textBoxRenderWidth.EditingFinished += this.textBoxRenderWidth_EditingFinished;
			this.textBoxRenderWidth.ProceedRequested += this.textBoxRenderWidth_ProceedRequested;

			this.textBoxRenderHeight = new ToolStripTextBoxAdv("textBoxRenderHeight");
			this.textBoxRenderHeight.BackColor = Color.FromArgb(196, 196, 196);
			this.textBoxRenderHeight.AutoSize = false;
			this.textBoxRenderHeight.Width = 35;
			this.textBoxRenderHeight.MaxLength = 4;
			this.textBoxRenderHeight.AcceptsOnlyNumbers = true;
			this.textBoxRenderHeight.EditingFinished += this.textBoxRenderHeight_EditingFinished;
			this.textBoxRenderHeight.ProceedRequested += this.textBoxRenderHeight_ProceedRequested;

			this.dropdownResolution = new ToolStripDropDownButton(CamViewResCache.IconMonitor);
			this.dropdownResolution.DropDownOpening += this.dropdownResolution_DropDownOpening;

			this.toolbarItems.Add(new ToolStripLabel("Window Size "));
			this.toolbarItems.Add(this.textBoxRenderWidth);
			this.toolbarItems.Add(new ToolStripLabel("x"));
			this.toolbarItems.Add(this.textBoxRenderHeight);
			this.toolbarItems.Add(this.dropdownResolution);
			
			this.resolutionMenuView = new MenuStripMenuView(this.dropdownResolution.DropDownItems);
			this.resolutionMenuView.Model = this.resolutionMenuModel;

			this.View.ToolbarCamera.SuspendLayout();
			for (int i = this.toolbarItems.Count - 1; i >= 0; i--)
			{
				ToolStripItem item = this.toolbarItems[i];
				item.Alignment = ToolStripItemAlignment.Right;
				this.View.ToolbarCamera.Items.Add(item);
			}
			this.View.ToolbarCamera.ResumeLayout();

			this.UpdateTargetRenderSizeUI();
		}
		private void RemoveToolbarItems()
		{
			this.View.ToolbarCamera.SuspendLayout();
			foreach (ToolStripItem item in this.toolbarItems)
			{
				this.View.ToolbarCamera.Items.Remove(item);
			}
			this.View.ToolbarCamera.ResumeLayout();

			this.textBoxRenderWidth.EditingFinished -= this.textBoxRenderWidth_EditingFinished;
			this.textBoxRenderWidth.ProceedRequested -= this.textBoxRenderWidth_ProceedRequested;
			this.textBoxRenderHeight.EditingFinished -= this.textBoxRenderHeight_EditingFinished;
			this.textBoxRenderHeight.ProceedRequested -= this.textBoxRenderHeight_ProceedRequested;
			this.dropdownResolution.DropDownOpening -= this.dropdownResolution_DropDownOpening;
			
			this.resolutionMenuView.Model = null;
			this.resolutionMenuView = null;
			this.resolutionMenuModel.ClearItems();

			this.toolbarItems.Clear();
			this.textBoxRenderWidth = null;
			this.textBoxRenderHeight = null;
		}
		private void InitResolutionDropDownItems()
		{
			// Remove old items
			this.resolutionMenuModel.ClearItems();

			// Add dynamic presets
			MenuModelItem gameViewItem = this.resolutionMenuModel.RequestItem("GameView Size");
			gameViewItem.ActionHandler = this.dropdownResolution_GameViewSizeClicked;
			gameViewItem.SortValue = MenuModelItem.SortValue_Top;
			gameViewItem.Checked = (this.targetRenderSizeMode == SpecialRenderSize.CamView);
			MenuModelItem targetSizeItem = this.resolutionMenuModel.RequestItem("Target Size");
			targetSizeItem.ActionHandler = this.dropdownResolution_TargetSizeClicked;
			targetSizeItem.SortValue = MenuModelItem.SortValue_Top;
			targetSizeItem.Checked = (this.targetRenderSizeMode == SpecialRenderSize.GameTarget);
			this.resolutionMenuModel.AddItem(new MenuModelItem
			{
				Name      = "TopSeparator",
				TypeHint  = MenuItemTypeHint.Separator,
				SortValue = MenuModelItem.SortValue_Top + 1
			});

			// Add fixed presets
			Point2[] fixedPresets = new Point2[]
			{
				new Point2(1920, 1080),
				new Point2(1280, 1024),
				new Point2(800, 600),
				new Point2(320, 300)
			};
			for (int i = 0; i < fixedPresets.Length; i++)
			{
				Point2 size = fixedPresets[i];
				string itemName = string.Format("{0} x {1}", size.X, size.Y);
				MenuModelItem item = this.resolutionMenuModel.RequestItem(itemName);
				item.Tag = size;
				item.ActionHandler = this.dropdownResolution_FixedSizeClicked;
				item.SortValue = MenuModelItem.SortValue_UnderTop + i;
				item.Checked = (this.TargetRenderSize == size);
			}
			this.resolutionMenuModel.AddItem(new MenuModelItem
			{
				Name      = "UnderTopSeparator",
				TypeHint  = MenuItemTypeHint.Separator,
				SortValue = MenuModelItem.SortValue_UnderTop + fixedPresets.Length + 1
			});

			// Add recently used custom fixed resolutions
			int addedItemCount = 0;
			for (int i = 0; i < this.recentTargetRenderSizes.Count; i++)
			{
				Point2 size = this.recentTargetRenderSizes[i];

				// Skip those that are already part of the fixed presets
				if (Array.IndexOf(fixedPresets, size) != -1)
					continue;

				string itemName = string.Format("{0} x {1}", size.X, size.Y);
				MenuModelItem item = this.resolutionMenuModel.RequestItem(itemName);
				item.Tag = size;
				item.ActionHandler = this.dropdownResolution_FixedSizeClicked;
				item.SortValue = MenuModelItem.SortValue_Main + i;
				item.Checked = (this.TargetRenderSize == size);

				// Add a maximum of three custom resolutions
				addedItemCount++;
				if (addedItemCount >= 3) break;
			}
		}

		private void ApplyTargetRenderSizeMode()
		{
			if (this.targetRenderSizeMode == SpecialRenderSize.CamView)
				this.TargetRenderSize = this.CamViewTargetSize;
			else if (this.targetRenderSizeMode == SpecialRenderSize.GameTarget)
				this.TargetRenderSize = this.GameTargetSize;
		}
		private void UpdateTargetRenderSizeUI()
		{
			this.isUpdatingUI = true;

			Color normalColor = Color.FromArgb(196, 196, 196);
			Color customSizeColor = Color.FromArgb(196, 224, 255);
			Color overSizedColor = Color.FromArgb(255, 196, 196);

			Color backColor;
			if (this.targetRenderSizeMode == SpecialRenderSize.CamView)
				backColor = normalColor;
			else if (this.TargetSizeFitsClientArea)
				backColor = customSizeColor;
			else
				backColor = overSizedColor;

			this.textBoxRenderWidth.Text = this.targetRenderSize.X.ToString();
			this.textBoxRenderHeight.Text = this.targetRenderSize.Y.ToString();
			this.textBoxRenderWidth.BackColor = backColor;
			this.textBoxRenderHeight.BackColor = backColor;

			this.isUpdatingUI = false;
		}
		private void ParseAndValidateTargetRenderSize()
		{
			int width;
			int height;
			if (!int.TryParse(this.textBoxRenderWidth.Text, out width))
				width = this.TargetRenderSize.X;
			if (!int.TryParse(this.textBoxRenderHeight.Text, out height))
				height = this.TargetRenderSize.Y;

			width = MathF.Clamp(width, 1, 3840);
			height = MathF.Clamp(height, 1, 2160);

			this.TargetRenderSizeMode = SpecialRenderSize.Fixed;
			this.TargetRenderSize = new Point2(width, height);
		}
		private void SampleRecentTargetRenderSize()
		{
			if (this.targetRenderSizeMode != SpecialRenderSize.Fixed)
				return;

			this.recentTargetRenderSizes.Remove(this.targetRenderSize);
			this.recentTargetRenderSizes.Insert(0, this.targetRenderSize);

			if (this.recentTargetRenderSizes.Count > 10)
				this.recentTargetRenderSizes.RemoveRange(10, this.recentTargetRenderSizes.Count - 10);
		}

		private void CleanupRenderTarget()
		{
			if (this.outputTarget != null)
			{
				this.outputTarget.Dispose();
				this.outputTarget = null;
			}
			if (this.outputTexture != null)
			{
				this.outputTexture.Dispose();
				this.outputTexture = null;
			}
		}
		private void SetupOutputRenderTarget()
		{
			if (this.outputTarget == null)
			{
				this.outputTexture = new Texture(
					1, 
					1, 
					TextureSizeMode.NonPowerOfTwo, 
					TextureMagFilter.Nearest, 
					TextureMinFilter.Linear);
				this.outputTarget = new RenderTarget();
				this.outputTarget.DepthBuffer = true;
				this.outputTarget.Targets = new ContentRef<Texture>[]
				{
					this.outputTexture
				};
			}

			Point2 outputSize = new Point2(this.TargetRenderSize.X, this.TargetRenderSize.Y);
			if (this.outputTarget.Size != outputSize)
			{
				this.outputTexture.Size = outputSize;
				this.outputTexture.ReloadData();

				this.outputTarget.Multisampling = 
					DualityApp.AppData.MultisampleBackBuffer ?
					DualityApp.UserData.AntialiasingQuality : 
					AAQuality.Off;
				this.outputTarget.SetupTarget();
			}
		}
		private void SetupBlitDevice()
		{
			if (this.blitDevice == null)
			{
				this.blitDevice = new DrawDevice();
				this.blitDevice.ClearFlags = ClearFlag.Depth;
				this.blitDevice.Perspective = PerspectiveMode.Flat;
				this.blitDevice.RenderMode = RenderMatrix.ScreenSpace;
			}
		}
		
		protected internal override void SaveUserData(XElement node)
		{
			base.SaveUserData(node);
			if (this.targetRenderSizeMode == SpecialRenderSize.Fixed)
			{
				XElement renderSizeElement = new XElement("RenderSize");
				renderSizeElement.SetElementValue("X", this.targetRenderSize.X);
				renderSizeElement.SetElementValue("Y", this.targetRenderSize.Y);
				node.Add(renderSizeElement);
			}
			else
			{
				node.Add(new XElement(
					"SpecialRenderSize", 
					this.targetRenderSizeMode));
			}

			XElement recentSizesElement = new XElement("RecentRenderSizes");
			foreach (Point2 recentSize in this.recentTargetRenderSizes)
			{
				recentSizesElement.Add(new XElement("RenderSize", 
					new XElement("X", recentSize.X),
					new XElement("Y", recentSize.Y)));
			}
			node.Add(recentSizesElement);
		}
		protected internal override void LoadUserData(XElement node)
		{
			base.LoadUserData(node);

			XElement renderSizeElement = node.Element("RenderSize");
			SpecialRenderSize specialSize = SpecialRenderSize.CamView;
			if (node.TryGetElementValue("SpecialRenderSize", ref specialSize) && specialSize != SpecialRenderSize.Fixed)
			{
				this.targetRenderSizeMode = specialSize;
			}
			else if (renderSizeElement != null)
			{
				this.targetRenderSizeMode = SpecialRenderSize.Fixed;
				this.targetRenderSize = new Point2(
					renderSizeElement.GetElementValue("X", this.targetRenderSize.X),
					renderSizeElement.GetElementValue("Y", this.targetRenderSize.Y));
			}

			XElement recentSizesElement = node.Element("RecentRenderSizes");
			if (recentSizesElement != null)
			{
				this.recentTargetRenderSizes.Clear();
				foreach (XElement item in recentSizesElement.Elements("RenderSize"))
				{
					Point2 recentSize = new Point2(
						item.GetElementValue("X", 0),
						item.GetElementValue("Y", 0));
					if (recentSize.X == 0) continue;
					if (recentSize.Y == 0) continue;

					this.recentTargetRenderSizes.Insert(0, recentSize);
				}
			}
		}

		protected internal override void OnEnterState()
		{
			base.OnEnterState();

			// Disable all regular editing functionality
			this.View.SetEditingToolsAvailable(false);
			this.CameraObj.Active = false;

			this.AddToolbarItems();
			this.ApplyTargetRenderSizeMode();
		}
		protected internal override void OnLeaveState()
		{
			base.OnLeaveState();

			this.RemoveToolbarItems();

			// Enable regular editing functionality again
			this.View.SetEditingToolsAvailable(true);
			this.CameraObj.Active = true;
		}
		protected override void OnRenderState()
		{
			// We're not calling the base implementation, because the default is to
			// render the scene from the view of an editing camera. The Game View, however, 
			// is in the special position to render the actual game and completely ignore 
			// any editing camera.
			//
			// base.OnRenderState();

			Point2 clientSize = new Point2(this.RenderableControl.ClientSize.Width, this.RenderableControl.ClientSize.Height);
			Point2 targetSize = this.TargetRenderSize;
			Rect windowRect = this.LocalGameWindowRect;
			
			Vector2 imageSize;
			Rect viewportRect;
			DualityApp.CalculateGameViewport(targetSize, out viewportRect, out imageSize);

			// Render the game view background using a background color matching editor UI,
			// so users can discern between an area that isn't rendered to and a rendered
			// area of the game that happens to be black or outside the game viewport.
			DrawDevice.RenderVoid(new Rect(clientSize), new ColorRgba(64, 64, 64));

			if (this.UseOffscreenBuffer)
			{
				// Render the scene to an offscreen buffer of matching size first
				this.SetupOutputRenderTarget();
				DualityApp.Render(this.outputTarget, viewportRect, imageSize);

				// Blit the offscreen buffer to the window area
				this.SetupBlitDevice();
				this.blitDevice.TargetSize = clientSize;
				this.blitDevice.ViewportRect = new Rect(clientSize);

				BatchInfo blitMaterial = new BatchInfo(
					DrawTechnique.Solid, 
					ColorRgba.White, 
					this.outputTexture);
				TargetResize blitResize = this.TargetSizeFitsClientArea ? 
					TargetResize.None : 
					TargetResize.Fit;

				this.blitDevice.PrepareForDrawcalls();
				this.blitDevice.AddFullscreenQuad(blitMaterial, blitResize);
				this.blitDevice.Render();
			}
			else
			{
				Rect windowViewportRect = new Rect(
					windowRect.X + viewportRect.X, 
					windowRect.Y + viewportRect.Y, 
					viewportRect.W, 
					viewportRect.H);

				// Render the scene centered into the designated viewport area
				this.CleanupRenderTarget();
				DrawDevice.RenderVoid(windowRect);
				DualityApp.Render(null, windowViewportRect, imageSize);
			}
		}
		protected override void OnResize()
		{
			base.OnResize();

			// Update target size when fitting to cam view size
			if (this.targetRenderSizeMode == SpecialRenderSize.CamView)
				this.TargetRenderSize = this.CamViewTargetSize;
			// Otherwise update the UI, because whether or not we're rendering at superresolution may have changed.
			else
				this.UpdateTargetRenderSizeUI();
		}
		protected override void OnGotFocus()
		{
			base.OnGotFocus();
			this.SampleRecentTargetRenderSize();
		}

		private void textBoxRenderWidth_ProceedRequested(object sender, EventArgs e)
		{
			this.textBoxRenderHeight.Focus();
		}
		private void textBoxRenderWidth_EditingFinished(object sender, EventArgs e)
		{
			if (this.isUpdatingUI) return;
			this.ParseAndValidateTargetRenderSize();
		}
		private void textBoxRenderHeight_ProceedRequested(object sender, EventArgs e)
		{
			this.textBoxRenderWidth.Focus();
		}
		private void textBoxRenderHeight_EditingFinished(object sender, EventArgs e)
		{
			if (this.isUpdatingUI) return;
			this.ParseAndValidateTargetRenderSize();
		}
		private void dropdownResolution_DropDownOpening(object sender, EventArgs e)
		{
			this.InitResolutionDropDownItems();
		}
		private void dropdownResolution_GameViewSizeClicked(object sender, EventArgs e)
		{
			this.TargetRenderSizeMode = SpecialRenderSize.CamView;
		}
		private void dropdownResolution_TargetSizeClicked(object sender, EventArgs e)
		{
			this.TargetRenderSizeMode = SpecialRenderSize.GameTarget;
		}
		private void dropdownResolution_FixedSizeClicked(object sender, EventArgs e)
		{
			MenuModelItem item = sender as MenuModelItem;
			Point2 size = (Point2)item.Tag;
			this.TargetRenderSizeMode = SpecialRenderSize.Fixed;
			this.TargetRenderSize = size;
		}
	}
}
