using com.basicGrowl.Seeker;

namespace com.basicGrowl.Seeker.Profiles
{
    internal static class SAM_IR1
    {
        public static void Register()
        {
            SeekerProfileRegistry.Register(new SeekerSoundProfile
            {
                PrefabName = "SAM_IR1",
                WeaponName = "IRM-S1",
                FolderName = "SeekerNoises",

                CagedFile = "Aim9PCaged.wav",
                UnCagedFile = "Aim9PUnCaged.wav",
                FlareReject = true,

                // Tune these to taste
                SeekerWeakPitch = 0.9f,
                SeekerStrongPitch = 1.0f,
            });
        }
    }
}
