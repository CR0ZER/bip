// Verification des mises a jour : lit le numero de la derniere release sur GitHub. Rien n'est
// telecharge ni installe ; le lien ouvert est toujours l'adresse fixe de la page des releases.
using System;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace ClaudeNotch
{
    static class Update
    {
        const string Api = "https://api.github.com/repos/CR0ZER/bip/releases/latest";
        public const string Page = "https://github.com/CR0ZER/bip/releases/latest";

        // "0.4.1", ou null : hors ligne, GitHub injoignable, reponse inattendue.
        public static string Latest()
        {
            try
            {
                ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;   // TLS 1.2 (.NET Framework 4)
                var rq = (HttpWebRequest)WebRequest.Create(Api);
                rq.UserAgent = "Bip/" + AppVersion.Text;
                rq.Accept = "application/vnd.github+json";
                rq.Timeout = rq.ReadWriteTimeout = 8000;
                using (var rs = rq.GetResponse())
                using (var r = new StreamReader(rs.GetResponseStream(), Encoding.UTF8))
                    return TagOf(r.ReadToEnd());
            }
            catch { return null; }
        }

        // Le tag de la reponse, s'il a la forme vX.Y.Z.
        public static string TagOf(string json)
        {
            try
            {
                var tag = Json.Str(Json.Obj(Json.Parse(json)), "tag_name") ?? "";
                var m = Regex.Match(tag, @"^v?(\d{1,4}\.\d{1,4}\.\d{1,4})$");
                return m.Success ? m.Groups[1].Value : null;
            }
            catch { return null; }
        }

        public static bool Newer(string latest, string current)
        {
            return latest != null && new Version(latest) > new Version(current);
        }
    }
}
