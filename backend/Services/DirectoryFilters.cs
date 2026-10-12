using System.Text;

namespace KiComLibBackend.Services
{
    public static class DirectoryFilters
    {
        public static string Escape(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return "";
            }

            StringBuilder builder = new(value.Length);
            foreach (char c in value)
            {
                switch (c)
                {
                    case '\\':
                        builder.Append("\\5c");
                        break;
                    case '*':
                        builder.Append("\\2a");
                        break;
                    case '(':
                        builder.Append("\\28");
                        break;
                    case ')':
                        builder.Append("\\29");
                        break;
                    case '\0':
                        builder.Append("\\00");
                        break;
                    default:
                        builder.Append(c);
                        break;
                }
            }

            return builder.ToString();
        }

        public static string LoginFilter(DirectorySettings settings, string username)
        {
            return settings.UserFilter.Replace("{username}", Escape(username), StringComparison.Ordinal);
        }

        public static string UserSearchFilter(DirectorySettings settings, string query)
        {
            string broad = settings.UserFilter.Replace("{username}", "*", StringComparison.Ordinal);
            string escaped = Escape(query.Trim());
            return $"(&{broad}(|({settings.UsernameAttribute}=*{escaped}*)({settings.DisplayNameAttribute}=*{escaped}*)(cn=*{escaped}*)))";
        }

        public static string GroupSearchFilter(DirectorySettings settings, string query)
        {
            string escaped = Escape(query.Trim());
            return $"(&{settings.GroupFilter}(|({settings.GroupNameAttribute}=*{escaped}*)(cn=*{escaped}*)))";
        }

        /// <summary>Active Directory ACCOUNTDISABLE is bit 2 of userAccountControl.</summary>
        public static bool IsDisabledAccount(string? userAccountControl)
        {
            return int.TryParse(userAccountControl, out int flags) && (flags & 2) != 0;
        }

        public static string NormalizeDn(string distinguishedName)
        {
            return distinguishedName.Trim().ToLowerInvariant();
        }
    }
}
