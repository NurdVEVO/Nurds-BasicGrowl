using com.basicGrowl.Seeker;

namespace com.basicGrowl.Seeker.Profiles
{
    internal static class AAM1
    {
        public static void Register()
        {
            SeekerProfileRegistry.Register(new SeekerSoundProfile
            {
                PrefabName = "AAM1",
                WeaponName = "MMR-S3",
                FolderName = "SeekerNoises",

                CagedFile = "Aim9Caged.wav",
                UnCagedFile = "Aim9UnCaged.wav",
                EnvSky = "Aim9EnvSky.wav",
                EnvGnd = "Aim9EnvGnd.wav",
                FlareReject = true,


                // Active in current heat/lock implementation.
                SeekerWeakPitch = 0.5f,
                SeekerStrongPitch = 1.0f,
            });
        }
    }
}
