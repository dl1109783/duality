﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Duality.Editor.PackageManagement
{
	/// <summary>
	/// Describes a Duality package that is part of the local <see cref="PackageSetup"/>.
	/// </summary>
	public sealed class LocalPackage
	{
		private PackageName packageName = PackageName.None;
		private PackageInfo info        = null;

		
		/// <summary>
		/// [GET] The name of the package, including ID and version number.
		/// Note that a local package may be version-invariant to flag itself
		/// for an update to the newest available version during package verify.
		/// </summary>
		public PackageName PackageName
		{
			get { return this.packageName; }
		}
		/// <summary>
		/// [GET] The ID of the package.
		/// </summary>
		public string Id
		{
			get { return this.packageName.Id; }
		}
		/// <summary>
		/// [GET] The version of the package. Can be null, in which case the
		/// package will be treated as version-invariant.
		/// </summary>
		public Version Version
		{
			get { return this.packageName.Version; }
		}
		/// <summary>
		/// [GET] A <see cref="PackageInfo"/> representing the locally installed
		/// version of the package.
		/// </summary>
		public PackageInfo Info
		{
			get { return this.info; }
			internal set { this.info = value; }
		}


		internal LocalPackage(PackageInfo info)
		{
			this.packageName = info.PackageName;
			this.info = info;
		}
		internal LocalPackage(PackageName package)
		{
			this.packageName = package;
			this.info = null;
		}

		public override string ToString()
		{
			return string.Format("Local Package '{0}'", this.packageName);
		}
	}
}
