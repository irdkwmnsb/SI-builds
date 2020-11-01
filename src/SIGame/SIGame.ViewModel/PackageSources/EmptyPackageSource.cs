﻿using SIGame.ViewModel.Properties;
using SIPackages;
using System;
using System.Threading.Tasks;

namespace SIGame.ViewModel.PackageSources
{
    /// <summary>
    /// Не выбранный источник пакета
    /// </summary>
    internal sealed class EmptyPackageSource: PackageSource
    {
        public override PackageSourceKey Key => null;

        public override string Source => $"({Resources.PackageNotSelected})";

        public override Task<(string, bool)> GetPackageFileAsync() => throw new NotImplementedException();

        public override string GetPackageName() => "";

        public override Task<byte[]> GetPackageHashAsync() => throw new NotImplementedException();
    }
}
