using UnityEngine;

public static class LocalPlayerProfile
{
    private const string NicknameKey = "practice.nickname";
    private static string cachedNickname;

    public static void SetNickname(string nickname)
    {
        cachedNickname = nickname == null ? string.Empty : nickname.Trim();
        PlayerPrefs.SetString(NicknameKey, cachedNickname);
        PlayerPrefs.Save();
    }

    public static string GetNickname()
    {
        if (cachedNickname != null)
        {
            return cachedNickname;
        }

        cachedNickname = PlayerPrefs.GetString(NicknameKey, string.Empty);
        return cachedNickname;
    }
}
