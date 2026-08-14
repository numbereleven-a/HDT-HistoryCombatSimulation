using System;
using System.Reflection;

namespace HistoryCombatSimulation
{
	public static class PluginVersion
	{
		private static readonly Assembly Assembly = typeof(PluginVersion).Assembly;
		public static readonly Version Hdt = CreateHdtVersion();
		public static readonly string Display = Hdt.ToString();
		public static readonly string LocalRelease = Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? Display;

		private static Version CreateHdtVersion()
		{
			var version = Assembly.GetName().Version ?? new Version(1, 0, 0, 0);
			var build = Math.Max(0, version.Build);
			return build == 0 ? new Version(version.Major, version.Minor) : new Version(version.Major, version.Minor, build);
		}
	}
}
