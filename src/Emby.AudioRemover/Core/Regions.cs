using System;
using System.Collections.Generic;
using System.Linq;

namespace Emby.AudioRemover.Core
{
    /// <summary>
    /// Countries and regions, as language labels name them: "English (United States)", "Português (Brasil)", "Spanish
    /// (Latinoamérica)", "pt-BR". They say which variety of a language a track is, so they're never suggested for the list.
    /// </summary>
    internal static class Regions
    {
        // English names, split into words: "United States" gives "United" and "States".
        private const string EnglishNames =
            "Afghanistan Albania Algeria Andorra Angola Antigua Barbuda Argentina Armenia Aruba Australia Austria Azerbaijan " +
            "Bahamas Bahrain Bangladesh Barbados Belarus Belgium Belize Benin Bermuda Bhutan Bolivia Bosnia Herzegovina Botswana " +
            "Brazil Brunei Bulgaria Burkina Faso Burundi Burma Cambodia Cameroon Canada Cape Verde Cabo Central African Republic " +
            "Chad Chile China Colombia Comoros Congo Costa Rica Croatia Cuba Curacao Cyprus Czechia Czech Denmark Djibouti " +
            "Dominica Dominican Ecuador Egypt El Salvador Equatorial Guinea Eritrea Estonia Eswatini Swaziland Ethiopia Fiji " +
            "Finland France Gabon Gambia Georgia Germany Ghana Gibraltar Greece Greenland Grenada Guadeloupe Guam Guatemala " +
            "Guernsey Guiana Bissau Guyana Haiti Honduras Hong Kong Hungary Iceland India Indonesia Iran Iraq Ireland Isle Man " +
            "Israel Italy Ivory Coast Jamaica Japan Jersey Jordan Kazakhstan Kenya Kiribati Korea Kosovo Kuwait Kyrgyzstan Laos " +
            "Latvia Lebanon Lesotho Liberia Libya Liechtenstein Lithuania Luxembourg Macau Macao Madagascar Malawi Malaysia " +
            "Maldives Mali Malta Marshall Islands Martinique Mauritania Mauritius Mexico Micronesia Moldova Monaco Mongolia " +
            "Montenegro Morocco Mozambique Myanmar Namibia Nauru Nepal Netherlands Holland New Zealand Caledonia Nicaragua Niger " +
            "Nigeria North Macedonia Norway Oman Pakistan Palau Palestine Panama Papua Paraguay Peru Philippines Poland Portugal " +
            "Puerto Rico Qatar Reunion Romania Russia Russian Federation Rwanda Saint Kitts Nevis Lucia Vincent Grenadines Samoa " +
            "San Marino Sao Tome Principe Saudi Arabia Senegal Serbia Seychelles Sierra Leone Singapore Slovakia Slovenia Solomon " +
            "Somalia South Africa Sudan Spain Sri Lanka Suriname Sweden Switzerland Syria Taiwan Tajikistan Tanzania Thailand " +
            "Timor Leste Togo Tonga Trinidad Tobago Tunisia Turkey Türkiye Turkmenistan Tuvalu Uganda Ukraine United Arab " +
            "Emirates Kingdom States America Uruguay Uzbekistan Vanuatu Vatican Venezuela Vietnam Viet Nam Yemen Zambia Zimbabwe " +
            "Britain Great England Scotland Wales Northern Faroe Catalonia Quebec Flanders Wallonia Bavaria Kurdistan";

        // Native and other common names, and words for regions rather than countries.
        private const string OtherNames =
            "Brasil España Espana México Méjico Deutschland Österreich Osterreich Schweiz Suisse Svizzera Belgique België Belgie " +
            "Nederland Italia Sverige Norge Danmark Suomi Polska Česko Cesko Česká Ceska Slovensko Magyarország Magyarorszag " +
            "România Hrvatska Srbija Србија Ελλάδα Ellada Россия Rossiya Україна Ukraina Беларусь Éire Eire Québec Canadá Perú " +
            "Estados Unidos Reino Unido Francia Frankreich Spanien Spagna Allemagne Alemania Germania Inglaterra Angleterre " +
            "Nippon Bharat भारत 中国 中國 台灣 台湾 香港 澳門 澳门 日本 한국 대한민국 Latinoamérica Latinoamerica Latam LatAm Iberoamérica " +
            "Iberoamerica Hispanoamérica Hispanoamerica Latina Americas Américas Europe Europa Asia Asie Africa Afrique Oceania " +
            "Middle East Mainland Iberian Hispanic Hispano Hant Hans Cyrl Latn SAR Parisian Brazillian Brasileiro Brasileira " +
            "Brasileño Canadien Canadienne Québécois Quebecois Latinoamericano Latinoamericana Europeo Europeu Européen Mexicano";

        // ISO 3166 two-letter codes, as in "pt-BR" or "Português (BR)", plus UK and EU.
        private const string Codes =
            "AD AE AF AG AI AL AM AO AQ AR AS AT AU AW AX AZ BA BB BD BE BF BG BH BI BJ BL BM BN BO BQ BR BS BT BV BW BY BZ CA CC " +
            "CD CF CG CH CI CK CL CM CN CO CR CU CV CW CX CY CZ DE DJ DK DM DO DZ EC EE EG EH ER ES ET FI FJ FK FM FO FR GA GB GD " +
            "GE GF GG GH GI GL GM GN GP GQ GR GS GT GU GW GY HK HM HN HR HT HU ID IE IL IM IN IO IQ IR IS IT JE JM JO JP KE KG KH " +
            "KI KM KN KP KR KW KY KZ LA LB LC LI LK LR LS LT LU LV LY MA MC MD ME MF MG MH MK ML MM MN MO MP MQ MR MS MT MU MV MW " +
            "MX MY MZ NA NC NE NF NG NI NL NO NP NR NU NZ OM PA PE PF PG PH PK PL PM PN PR PS PT PW PY QA RE RO RS RU RW SA SB SC " +
            "SD SE SG SH SI SJ SK SL SM SN SO SR SS ST SV SX SY SZ TC TD TF TG TH TJ TK TL TM TN TO TR TT TV TW TZ UA UG UM US UY " +
            "UZ VA VC VE VG VI VN VU WF WS YE YT ZA ZM ZW UK EU";

        private static readonly HashSet<string> Words = new HashSet<string>(
            (EnglishNames + " " + OtherNames + " " + Codes).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries),
            StringComparer.OrdinalIgnoreCase);

        public static bool Contains(string word) => Words.Contains(word);

        /// <summary>How many words the list has, for the tests.</summary>
        internal static int Count => Words.Count;

        internal static IEnumerable<string> All => Words.OrderBy(w => w, StringComparer.OrdinalIgnoreCase);
    }
}
