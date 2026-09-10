using com.basicGrowl.Seeker;

namespace com.basicGrowl.Seeker.Profiles
{
    internal static class AAM3
    {
        public static void Register()
        {
            SeekerProfileRegistry.Register(new SeekerSoundProfile
            {
                PrefabName = "AAM3",
                WeaponName = "IRM-S2",
                FolderName = "SeekerNoises",

                CagedFile = "Aim9Caged.wav",
                UnCagedFile = "Aim9UnCaged.wav",
                EnvSky = "Aim9EnvSky.wav",
                EnvGnd = "Aim9EnvGnd.wav",
                FlareReject = true,

                // Tune these to taste
                SeekerWeakPitch = 0.5f,
                SeekerStrongPitch = 1.0f,
            });
        }
    }
}
