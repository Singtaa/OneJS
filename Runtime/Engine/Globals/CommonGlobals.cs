namespace OneJS {
    public class CommonGlobals {
        public static string atob(string str) {
            return System.Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(str));
        }
        
        public static string btoa(string str) {
            return System.Text.Encoding.UTF8.GetString(System.Convert.FromBase64String(str));
        }
    }
}