// Modifiers: 장비 등급(빛바랜/은은한/찬란한) · 랜덤 추가 옵션 · 유니크 특수 능력 모드
// Spark in the Dark (Unity Mono) - Assembly-CSharp 에 Cecil 로 훅을 삽입해 호출된다.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Creatures;
using HandItems;
using Management;
using UI;
using UnityEngine;
using UnityEngine.UI;
using ModLoader;

[assembly: ModInfo("Modifiers", "1.7.1", Description = "장비에 빛바랜/은은한/찬란한 등급, 랜덤 추가 옵션, 유니크 특수 능력을 붙입니다.", SettingsNote = "바꾼 값은 바로 저장되고 적용됩니다. 확률·옵션 설정은 앞으로 새로 정해지는 장비부터, 유니크 능력 수치는 이미 가진 장비에도 바로 반영됩니다. 옵션·유니크의 체크를 끄면 새로 나오지 않을 뿐, 이미 붙은 것은 그대로 남습니다.")]

namespace Modifiers
{
    public enum AffixKind
    {
        Strength = 0, Dexterity = 1, Perception = 2, Constitution = 3, Will = 4,
        Health = 5, Stamina = 6, Recovery = 7, MaxWeight = 8, Vision = 9, Speed = 10,
        Accuracy = 11, Dodge = 12, Stealth = 13, Damage = 14, Defence = 15,
        WeightReduction = 16, PoisonResist = 17, DarknessResist = 18
    }

    public struct Affix
    {
        public AffixKind Kind;
        public int Value;
    }

    public class ModData
    {
        public int Tier;            // 0 = 빛바랜, 1 = 은은한, 2 = 찬란한
        public int Unique;          // 0 = 없음, 1.. = UniqueDef.Id
        public List<Affix> Affixes = new List<Affix>();

        public string Serialize()
        {
            var sb = new StringBuilder();
            sb.Append("v1;t=").Append(Tier).Append(";u=").Append(Unique).Append(";a=");
            for (int i = 0; i < Affixes.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append((int)Affixes[i].Kind).Append(':').Append(Affixes[i].Value);
            }
            return sb.ToString();
        }

        public static ModData Parse(string s)
        {
            if (string.IsNullOrEmpty(s) || !s.StartsWith("v1;", StringComparison.Ordinal)) return null;
            var d = new ModData();
            foreach (var part in s.Split(';'))
            {
                int eq = part.IndexOf('=');
                if (eq <= 0) continue;
                string k = part.Substring(0, eq), v = part.Substring(eq + 1);
                if (k == "t") d.Tier = Mathf.Clamp(ParseInt(v), 0, 2);
                else if (k == "u") d.Unique = ParseInt(v);
                else if (k == "a" && v.Length > 0)
                {
                    foreach (var a in v.Split(','))
                    {
                        int c = a.IndexOf(':');
                        if (c <= 0) continue;
                        int kind = ParseInt(a.Substring(0, c));
                        if (kind < 0 || kind > (int)AffixKind.DarknessResist) continue;
                        d.Affixes.Add(new Affix { Kind = (AffixKind)kind, Value = ParseInt(a.Substring(c + 1)) });
                    }
                }
            }
            if (d.Unique != 0 && Mod.GetUnique(d.Unique) == null) d.Unique = 0;
            return d;
        }

        static int ParseInt(string s)
        {
            int r;
            int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out r);
            return r;
        }
    }

    public class UniqueDef
    {
        public int Id;
        public string Name;
        public int KindMask;        // 1 = 무기, 2 = 갑옷, 4 = 방패
        public Func<string> DescF;
        public Func<bool> On;
        public Func<Affix[]> StatsF;
        public bool BossOnly;       // 보스만 떨어뜨림 (무작위로 나오지 않음)
        public string ItemName;     // 정해진 장비 이름 (보스 전용 장비)
        public string Desc { get { return DescF != null ? DescF() : ""; } }
        public Affix[] Stats { get { return StatsF != null ? StatsF() : new Affix[0]; } }
    }

    public class AffixDef
    {
        public AffixKind Kind;
        public string Name;
        public bool Percent;
        public int Min, Max;
        public int KindMask;
        public CharSpecType Target;
        public BuffSpecialEffect Effect;
        public bool Negative;       // 표시 부호가 "-" (무게 감소)
    }

    // 세이브 호환을 위해 저장 키 이름(SpkMod)은 예전 그대로 유지한다
    [Serializable]
    public class SaveWrap
    {
        public string SpkMod;
    }

    // 모드 로더의 [모드] > [설정] 화면에 나타나는 값들. 여기 적힌 값이 기본값이다.
    public static class Cfg
    {
        const string S0 = "적용 대상", S1 = "등급 확률", S3 = "추가 옵션 개수", S4 = "추가 옵션 종류 (체크 · 최소 · 최대)", S5 = "유니크", S6 = "유니크 능력 (체크 · 수치)";

        [Setting(S0, "무기", Desc = "체크하면 무기에 등급/옵션이 붙습니다. (석궁·맨손·부러진 무기·뼈 무기는 항상 제외)")] public static bool ApplyWeapons = true;
        [Setting(S0, "갑옷", Inline = true, Desc = "체크하면 갑옷에 등급/옵션이 붙습니다.")] public static bool ApplyArmor = true;
        [Setting(S0, "방패", Inline = true, Desc = "체크하면 방패에 등급/옵션이 붙습니다.")] public static bool ApplyShields = true;

        [Setting(S1, "빛바랜 비중", Min = 0, Max = 100, Desc = "세 값의 비율대로 등급이 정해집니다. 기본 35 : 45 : 20 → 빛바랜 35%, 은은한 45%, 찬란한 20% (유니크가 아닐 때)")] public static int TierWeightLow = 35;
        [Setting(S1, "은은한 비중", Min = 0, Max = 100, Desc = "세 값의 비율대로 등급이 정해집니다. 합이 100일 필요는 없습니다.")] public static int TierWeightMid = 45;
        [Setting(S1, "찬란한 비중", Min = 0, Max = 100, Desc = "세 값의 비율대로 등급이 정해집니다. 합이 100일 필요는 없습니다.")] public static int TierWeightHigh = 20;

        // 등급별 기본 성능 보정(%) - 무기 피해량·양손무기 막기·갑옷/방패 방어력 (설정 화면에는 노출하지 않음)
        public static int TierPowerLow = -10;
        public static int TierPowerMid = 0;
        public static int TierPowerHigh = 12;
        public static int UniquePower = 12;

        [Setting(S3, "빛바랜  최소", Unit = "개", Min = 0, Max = 6, Desc = "빛바랜 등급에 붙는 추가 옵션 개수. 최소~최대 사이에서 균등하게 정해집니다.")] public static int OptLowMin = 0;
        [Setting(S3, "최대", Unit = "개", Min = 0, Max = 6, Inline = true)] public static int OptLowMax = 1;
        [Setting(S3, "은은한  최소", Unit = "개", Min = 0, Max = 6, Desc = "은은한 등급에 붙는 추가 옵션 개수")] public static int OptMidMin = 1;
        [Setting(S3, "최대", Unit = "개", Min = 0, Max = 6, Inline = true)] public static int OptMidMax = 2;
        [Setting(S3, "찬란한  최소", Unit = "개", Min = 0, Max = 6, Desc = "찬란한 등급에 붙는 추가 옵션 개수")] public static int OptHighMin = 2;
        [Setting(S3, "최대", Unit = "개", Min = 0, Max = 6, Inline = true)] public static int OptHighMax = 3;
        [Setting(S3, "능력치 옵션 최대값 확률 (찬란한)", Unit = "%", Min = 0, Max = 100, Desc = "근력·민첩성 같은 능력치 옵션은 보통 최소값이 붙고, 찬란한 등급(유니크 포함)일 때 이 확률로 최대값이 붙습니다.")] public static int AttrMaxChance = 25;
        [Setting(S4, "근력", Desc = "근력 옵션 (모든 장비). 빛바랜 등급은 범위의 아래쪽 절반, 은은한 등급은 가운데, 찬란한 등급은 위쪽 절반에서 수치가 정해집니다.")] public static bool AffixStrengthOn = true;
        [Setting(S4, "최소", Unit = "", Min = 1, Max = 10, Inline = true)] public static int AffixStrengthMin = 1;
        [Setting(S4, "최대", Unit = "", Min = 1, Max = 10, Inline = true)] public static int AffixStrengthMax = 2;
        [Setting(S4, "민첩성", Desc = "민첩성 옵션 (모든 장비). 빛바랜 등급은 범위의 아래쪽 절반, 은은한 등급은 가운데, 찬란한 등급은 위쪽 절반에서 수치가 정해집니다.")] public static bool AffixDexterityOn = true;
        [Setting(S4, "최소", Unit = "", Min = 1, Max = 10, Inline = true)] public static int AffixDexterityMin = 1;
        [Setting(S4, "최대", Unit = "", Min = 1, Max = 10, Inline = true)] public static int AffixDexterityMax = 2;
        [Setting(S4, "통찰력", Desc = "통찰력 옵션 (모든 장비). 빛바랜 등급은 범위의 아래쪽 절반, 은은한 등급은 가운데, 찬란한 등급은 위쪽 절반에서 수치가 정해집니다.")] public static bool AffixPerceptionOn = true;
        [Setting(S4, "최소", Unit = "", Min = 1, Max = 10, Inline = true)] public static int AffixPerceptionMin = 1;
        [Setting(S4, "최대", Unit = "", Min = 1, Max = 10, Inline = true)] public static int AffixPerceptionMax = 2;
        [Setting(S4, "체질", Desc = "체질 옵션 (모든 장비). 빛바랜 등급은 범위의 아래쪽 절반, 은은한 등급은 가운데, 찬란한 등급은 위쪽 절반에서 수치가 정해집니다.")] public static bool AffixConstitutionOn = true;
        [Setting(S4, "최소", Unit = "", Min = 1, Max = 10, Inline = true)] public static int AffixConstitutionMin = 1;
        [Setting(S4, "최대", Unit = "", Min = 1, Max = 10, Inline = true)] public static int AffixConstitutionMax = 2;
        [Setting(S4, "의지력", Desc = "의지력 옵션 (모든 장비). 빛바랜 등급은 범위의 아래쪽 절반, 은은한 등급은 가운데, 찬란한 등급은 위쪽 절반에서 수치가 정해집니다.")] public static bool AffixWillOn = true;
        [Setting(S4, "최소", Unit = "", Min = 1, Max = 10, Inline = true)] public static int AffixWillMin = 1;
        [Setting(S4, "최대", Unit = "", Min = 1, Max = 10, Inline = true)] public static int AffixWillMax = 2;
        [Setting(S4, "최대 체력", Desc = "최대 체력 옵션 (갑옷·방패). 빛바랜 등급은 범위의 아래쪽 절반, 은은한 등급은 가운데, 찬란한 등급은 위쪽 절반에서 수치가 정해집니다.")] public static bool AffixHealthOn = true;
        [Setting(S4, "최소", Unit = "%", Min = 1, Max = 100, Inline = true)] public static int AffixHealthMin = 4;
        [Setting(S4, "최대", Unit = "%", Min = 1, Max = 100, Inline = true)] public static int AffixHealthMax = 10;
        [Setting(S4, "최대 스태미나", Desc = "최대 스태미나 옵션 (모든 장비). 빛바랜 등급은 범위의 아래쪽 절반, 은은한 등급은 가운데, 찬란한 등급은 위쪽 절반에서 수치가 정해집니다.")] public static bool AffixStaminaOn = true;
        [Setting(S4, "최소", Unit = "%", Min = 1, Max = 100, Inline = true)] public static int AffixStaminaMin = 5;
        [Setting(S4, "최대", Unit = "%", Min = 1, Max = 100, Inline = true)] public static int AffixStaminaMax = 12;
        [Setting(S4, "회복력", Desc = "회복력 옵션 (모든 장비). 빛바랜 등급은 범위의 아래쪽 절반, 은은한 등급은 가운데, 찬란한 등급은 위쪽 절반에서 수치가 정해집니다.")] public static bool AffixRecoveryOn = true;
        [Setting(S4, "최소", Unit = "%", Min = 1, Max = 100, Inline = true)] public static int AffixRecoveryMin = 6;
        [Setting(S4, "최대", Unit = "%", Min = 1, Max = 100, Inline = true)] public static int AffixRecoveryMax = 15;
        [Setting(S4, "최대 소지 무게", Desc = "최대 소지 무게 옵션 (갑옷). 빛바랜 등급은 범위의 아래쪽 절반, 은은한 등급은 가운데, 찬란한 등급은 위쪽 절반에서 수치가 정해집니다.")] public static bool AffixMaxWeightOn = true;
        [Setting(S4, "최소", Unit = "%", Min = 1, Max = 100, Inline = true)] public static int AffixMaxWeightMin = 6;
        [Setting(S4, "최대", Unit = "%", Min = 1, Max = 100, Inline = true)] public static int AffixMaxWeightMax = 15;
        [Setting(S4, "시야", Desc = "시야 옵션 (모든 장비). 빛바랜 등급은 범위의 아래쪽 절반, 은은한 등급은 가운데, 찬란한 등급은 위쪽 절반에서 수치가 정해집니다.")] public static bool AffixVisionOn = true;
        [Setting(S4, "최소", Unit = "%", Min = 1, Max = 100, Inline = true)] public static int AffixVisionMin = 4;
        [Setting(S4, "최대", Unit = "%", Min = 1, Max = 100, Inline = true)] public static int AffixVisionMax = 10;
        [Setting(S4, "기동성", Desc = "기동성 옵션 (갑옷). 빛바랜 등급은 범위의 아래쪽 절반, 은은한 등급은 가운데, 찬란한 등급은 위쪽 절반에서 수치가 정해집니다.")] public static bool AffixSpeedOn = true;
        [Setting(S4, "최소", Unit = "%", Min = 1, Max = 100, Inline = true)] public static int AffixSpeedMin = 2;
        [Setting(S4, "최대", Unit = "%", Min = 1, Max = 100, Inline = true)] public static int AffixSpeedMax = 5;
        [Setting(S4, "명중률", Desc = "명중률 옵션 (무기). 빛바랜 등급은 범위의 아래쪽 절반, 은은한 등급은 가운데, 찬란한 등급은 위쪽 절반에서 수치가 정해집니다.")] public static bool AffixAccuracyOn = true;
        [Setting(S4, "최소", Unit = "%", Min = 1, Max = 100, Inline = true)] public static int AffixAccuracyMin = 4;
        [Setting(S4, "최대", Unit = "%", Min = 1, Max = 100, Inline = true)] public static int AffixAccuracyMax = 10;
        [Setting(S4, "회피율", Desc = "회피율 옵션 (갑옷·방패). 빛바랜 등급은 범위의 아래쪽 절반, 은은한 등급은 가운데, 찬란한 등급은 위쪽 절반에서 수치가 정해집니다.")] public static bool AffixDodgeOn = true;
        [Setting(S4, "최소", Unit = "%", Min = 1, Max = 100, Inline = true)] public static int AffixDodgeMin = 4;
        [Setting(S4, "최대", Unit = "%", Min = 1, Max = 100, Inline = true)] public static int AffixDodgeMax = 10;
        [Setting(S4, "은신", Desc = "은신 옵션 (갑옷). 빛바랜 등급은 범위의 아래쪽 절반, 은은한 등급은 가운데, 찬란한 등급은 위쪽 절반에서 수치가 정해집니다.")] public static bool AffixStealthOn = true;
        [Setting(S4, "최소", Unit = "%", Min = 1, Max = 100, Inline = true)] public static int AffixStealthMin = 5;
        [Setting(S4, "최대", Unit = "%", Min = 1, Max = 100, Inline = true)] public static int AffixStealthMax = 12;
        [Setting(S4, "피해량", Desc = "피해량 옵션 (무기). 빛바랜 등급은 범위의 아래쪽 절반, 은은한 등급은 가운데, 찬란한 등급은 위쪽 절반에서 수치가 정해집니다.")] public static bool AffixDamageOn = true;
        [Setting(S4, "최소", Unit = "%", Min = 1, Max = 100, Inline = true)] public static int AffixDamageMin = 3;
        [Setting(S4, "최대", Unit = "%", Min = 1, Max = 100, Inline = true)] public static int AffixDamageMax = 8;
        [Setting(S4, "방어력", Desc = "방어력 옵션 (갑옷). 빛바랜 등급은 범위의 아래쪽 절반, 은은한 등급은 가운데, 찬란한 등급은 위쪽 절반에서 수치가 정해집니다.")] public static bool AffixDefenceOn = true;
        [Setting(S4, "최소", Unit = "%", Min = 1, Max = 100, Inline = true)] public static int AffixDefenceMin = 4;
        [Setting(S4, "최대", Unit = "%", Min = 1, Max = 100, Inline = true)] public static int AffixDefenceMax = 10;
        [Setting(S4, "무게 감소", Desc = "무게 감소 옵션 (모든 장비). 빛바랜 등급은 범위의 아래쪽 절반, 은은한 등급은 가운데, 찬란한 등급은 위쪽 절반에서 수치가 정해집니다.")] public static bool AffixWeightReductionOn = true;
        [Setting(S4, "최소", Unit = "%", Min = 1, Max = 100, Inline = true)] public static int AffixWeightReductionMin = 10;
        [Setting(S4, "최대", Unit = "%", Min = 1, Max = 100, Inline = true)] public static int AffixWeightReductionMax = 25;
        [Setting(S4, "독 저항력", Desc = "독 저항력 옵션 (모든 장비). 빛바랜 등급은 범위의 아래쪽 절반, 은은한 등급은 가운데, 찬란한 등급은 위쪽 절반에서 수치가 정해집니다.")] public static bool AffixPoisonResistOn = true;
        [Setting(S4, "최소", Unit = "%", Min = 1, Max = 100, Inline = true)] public static int AffixPoisonResistMin = 8;
        [Setting(S4, "최대", Unit = "%", Min = 1, Max = 100, Inline = true)] public static int AffixPoisonResistMax = 20;
        [Setting(S4, "어둠 저항력", Desc = "어둠 저항력 옵션 (모든 장비). 빛바랜 등급은 범위의 아래쪽 절반, 은은한 등급은 가운데, 찬란한 등급은 위쪽 절반에서 수치가 정해집니다.")] public static bool AffixDarknessResistOn = true;
        [Setting(S4, "최소", Unit = "%", Min = 1, Max = 100, Inline = true)] public static int AffixDarknessResistMin = 8;
        [Setting(S4, "최대", Unit = "%", Min = 1, Max = 100, Inline = true)] public static int AffixDarknessResistMax = 20;

        [Setting(S5, "유니크 등장 확률", Unit = "%", Min = 0, Max = 100, Step = 0.5f, Desc = "장비 하나의 등급이 정해질 때 유니크가 될 확률. 나올 수 있는 유니크가 없으면 일반 등급으로 정해집니다.")] public static float UniqueChance = 5f;
        [Setting(S5, "유니크 옵션  최소", Unit = "개", Min = 0, Max = 6, Desc = "유니크에 함께 붙는 일반 추가 옵션 개수")] public static int OptUniqueMin = 2;
        [Setting(S5, "최대", Unit = "개", Min = 0, Max = 6, Inline = true)] public static int OptUniqueMax = 3;
        [Setting(S5, "지니고 있는 유니크는 다시 나오지 않음", Desc = "영웅 소지품(장착·가방)에 있는 유니크는 새로 나오지 않습니다.")] public static bool BlockHeldUniques = true;
        [Setting(S5, "같은 층에 이미 나온 유니크는 다시 나오지 않음", Desc = "현재 층 어딘가(바닥·상자·시체)에 이미 생긴 유니크는 같은 층에서 또 나오지 않습니다.")] public static bool BlockFloorUniques = true;

        [Setting(S6, "피의 갈증 (무기)", Desc = "근접 공격으로 입힌 피해의 일정 %만큼 체력을 흡수합니다.")] public static bool UBloodthirstOn = true;
        [Setting(S6, "흡수", Unit = "%", Min = 1, Max = 100, Inline = true)] public static int UBloodthirstPct = 10;
        [Setting(S6, "처형인의 결의 (무기)", Desc = "체력이 기준 % 이하인 적에게 주는 피해가 늘어납니다.")] public static bool UExecutionerOn = true;
        [Setting(S6, "기준", Unit = "%", Min = 1, Max = 100, Inline = true)] public static int UExecutionerHp = 30;
        [Setting(S6, "피해", Unit = "%", Min = 1, Max = 300, Inline = true)] public static int UExecutionerDmg = 40;
        [Setting(S6, "폭풍의 춤 (무기)", Desc = "공격할 때 소모하는 스태미나가 줄어듭니다.")] public static bool UTempestOn = true;
        [Setting(S6, "절약", Unit = "%", Min = 1, Max = 90, Inline = true)] public static int UTempestPct = 35;
        [Setting(S6, "영혼 수확자 (무기)", Desc = "적을 처치하면 최대 체력/스태미나의 일정 %를 회복합니다.")] public static bool UReaperOn = true;
        [Setting(S6, "체력", Unit = "%", Min = 0, Max = 100, Inline = true)] public static int UReaperHp = 8;
        [Setting(S6, "스태미나", Unit = "%", Min = 0, Max = 100, Inline = true)] public static int UReaperSt = 25;
        [Setting(S6, "광전사의 분노 (무기)", Desc = "자신의 체력이 기준 % 이하일 때 주는 근접 피해가 늘어납니다.")] public static bool UBerserkerOn = true;
        [Setting(S6, "기준", Unit = "%", Min = 1, Max = 100, Inline = true)] public static int UBerserkerHp = 50;
        [Setting(S6, "피해", Unit = "%", Min = 1, Max = 300, Inline = true)] public static int UBerserkerDmg = 25;
        [Setting(S6, "질풍의 손놀림 (무기)", Desc = "공격 동작이 빨라집니다. 공격 한 번에 드는 스태미나는 그대로라 스태미나가 더 빨리 줄어듭니다.")] public static bool UGaleOn = true;
        [Setting(S6, "속도", Unit = "%", Min = 1, Max = 50, Inline = true)] public static int UGalePct = 15;
        [Setting(S6, "불굴의 맹세 (갑옷)", Desc = "자신의 체력이 기준 % 이하일 때 받는 피해가 줄어듭니다.")] public static bool UUndyingOn = true;
        [Setting(S6, "기준", Unit = "%", Min = 1, Max = 100, Inline = true)] public static int UUndyingHp = 35;
        [Setting(S6, "감소", Unit = "%", Min = 1, Max = 90, Inline = true)] public static int UUndyingPct = 35;
        [Setting(S6, "재생의 숨결 (갑옷)", Desc = "매초 최대 체력의 일정 %를 회복합니다.")] public static bool URegenOn = true;
        [Setting(S6, "초당", Unit = "%", Min = 0.1f, Max = 5, Step = 0.1f, Inline = true)] public static float URegenPct = 0.3f;
        [Setting(S6, "그림자 걸음 (갑옷)", Desc = "은신과 회피율이 오릅니다.")] public static bool UShadowOn = true;
        [Setting(S6, "은신", Unit = "%", Min = 0, Max = 100, Inline = true)] public static int UShadowStealth = 25;
        [Setting(S6, "회피", Unit = "%", Min = 0, Max = 100, Inline = true)] public static int UShadowDodge = 12;
        [Setting(S6, "가시 방패 (방패)", Desc = "방패로 정면 공격을 막으면 공격 피해의 일정 %를 공격자에게 되돌려줍니다. (적의 체력을 1 미만으로 만들지는 않음)")] public static bool UThornsOn = true;
        [Setting(S6, "반사", Unit = "%", Min = 1, Max = 200, Inline = true)] public static int UThornsPct = 30;
        [Setting(S6, "수호자의 맹세 (방패)", Desc = "받는 모든 피해가 줄어듭니다.")] public static bool UGuardianOn = true;
        [Setting(S6, "감소", Unit = "%", Min = 1, Max = 90, Inline = true)] public static int UGuardianPct = 12;
        [Setting(S6, "별빛의 가호 (공통)", Desc = "어둠 저항력이 오릅니다.")] public static bool UStarlightOn = true;
        [Setting(S6, "어둠", Unit = "%", Min = 1, Max = 100, Inline = true)] public static int UStarlightPct = 35;
        [Setting(S6, "원혼의 불꽃 (보스 전용 무기)", Desc = "리빙 아머 발로르가 떨어뜨리는 대검의 능력. 맞은 적에게 출혈보다 강한 지속 피해와 이동 속도 감소를 겁니다. 맞은 몸에 푸른 불꽃이 붙습니다. 값은 이동 속도 감소량.", Unit = "%", Min = 0, Max = 50)] public static int USoulFlameSlow = 10;
        [Setting(S6, "지속", Unit = "초", Min = 2, Max = 20, Inline = true)] public static int USoulFlameSec = 8;
        [Setting(S6, "해독의 축복 (공통)", Desc = "독 저항력이 오릅니다.")] public static bool UAntidoteOn = true;
        [Setting(S6, "독", Unit = "%", Min = 1, Max = 100, Inline = true)] public static int UAntidotePct = 50;

        // ---- 이름으로 옵션 설정 읽기 (옵션 굴릴 때만 사용)
        static readonly System.Collections.Generic.Dictionary<string, System.Reflection.FieldInfo> Cache = new System.Collections.Generic.Dictionary<string, System.Reflection.FieldInfo>();
        static object F(string name)
        {
            System.Reflection.FieldInfo f;
            if (!Cache.TryGetValue(name, out f)) { f = typeof(Cfg).GetField(name); Cache[name] = f; }
            return f == null ? null : f.GetValue(null);
        }
        public static bool AffixOn(AffixKind k) { object v = F("Affix" + k + "On"); return v == null || (bool)v; }
        public static void AffixRange(AffixKind k, int defMin, int defMax, out int min, out int max)
        {
            object a = F("Affix" + k + "Min"), b = F("Affix" + k + "Max");
            min = a == null ? defMin : (int)a; max = b == null ? defMax : (int)b;
            if (min > max) { int t = min; min = max; max = t; }
        }
        public static void Range(int a, int b, out int min, out int max) { min = Math.Min(a, b); max = Math.Max(a, b); }
    }

    public static class Mod
    {
        public const string BuffPrefix = "SPKMOD|";
        const string MeleeDamaged = "CharacterBase,Weapon,EnemySector,Armor,Shield,HandItem";
        public const int KindWeapon = 1, KindArmor = 2, KindShield = 4, KindAny = 7;

        static readonly string[] TierName = { "빛바랜", "은은한", "찬란한" };

        static int TierPowerPct(ModData d)
        {
            if (d.Unique != 0) return Cfg.UniquePower;
            return d.Tier == 0 ? Cfg.TierPowerLow : (d.Tier == 1 ? Cfg.TierPowerMid : Cfg.TierPowerHigh);
        }

        static readonly System.Random Rng = new System.Random();
        static readonly Dictionary<HandItem, ModData> Data = new Dictionary<HandItem, ModData>();
        static readonly Dictionary<string, BuffInfo> BuffCache = new Dictionary<string, BuffInfo>();

        static int _activeUniques;          // 장착 중인 유니크 비트마스크
        static float _nextCleanup;

        class BuffInfo
        {
            public float Value;
            public string Desc;
        }

        public static readonly AffixDef[] Affixes =
        {
            A(AffixKind.Strength,     "근력",          false, 1, 2,  KindAny,    CharSpecType.Strength),
            A(AffixKind.Dexterity,    "민첩성",        false, 1, 2,  KindAny,    CharSpecType.Dexterity),
            A(AffixKind.Perception,   "통찰력",        false, 1, 2,  KindAny,    CharSpecType.Perception),
            A(AffixKind.Constitution, "체질",          false, 1, 2,  KindAny,    CharSpecType.Constitution),
            A(AffixKind.Will,         "의지력",        false, 1, 2,  KindAny,    CharSpecType.Will),
            A(AffixKind.Health,       "최대 체력",     true,  4, 10, KindArmor | KindShield, CharSpecType.Health),
            A(AffixKind.Stamina,      "최대 스태미나", true,  5, 12, KindAny,    CharSpecType.Stamina),
            A(AffixKind.Recovery,     "회복력",        true,  6, 15, KindAny,    CharSpecType.Recovery),
            A(AffixKind.MaxWeight,    "최대 소지 무게", true, 6, 15, KindArmor,  CharSpecType.MaxWeight),
            A(AffixKind.Vision,       "시야",          true,  4, 10, KindAny,    CharSpecType.VisionRange),
            A(AffixKind.Speed,        "기동성",        true,  2, 5,  KindArmor,  CharSpecType.Speed),
            A(AffixKind.Accuracy,     "명중률",        true,  4, 10, KindWeapon, CharSpecType.Accuracy),
            A(AffixKind.Dodge,        "회피율",        true,  4, 10, KindArmor | KindShield, CharSpecType.Dodge),
            A(AffixKind.Stealth,      "은신",          true,  5, 12, KindArmor,  CharSpecType.NoiseLessness),
            A(AffixKind.Damage,       "피해량",        true,  3, 8,  KindWeapon, CharSpecType.DamageMelee),
            A(AffixKind.Defence,      "방어력",        true,  4, 10, KindArmor,  CharSpecType.Defence),
            new AffixDef { Kind = AffixKind.WeightReduction, Name = "무게", Percent = true, Min = 10, Max = 25, KindMask = KindAny, Target = CharSpecType.None, Negative = true },
            new AffixDef { Kind = AffixKind.PoisonResist,   Name = "독 저항력",   Percent = true, Min = 8, Max = 20, KindMask = KindAny, Target = CharSpecType.None, Effect = BuffSpecialEffect.PoisonResist },
            new AffixDef { Kind = AffixKind.DarknessResist, Name = "어둠 저항력", Percent = true, Min = 8, Max = 20, KindMask = KindAny, Target = CharSpecType.None, Effect = BuffSpecialEffect.DarknessResist },
        };

        public const int U_Bloodthirst = 1, U_Executioner = 2, U_Tempest = 3, U_Reaper = 4, U_Berserker = 5,
            U_Undying = 6, U_Regeneration = 7, U_Shadow = 8, U_Thorns = 9, U_Guardian = 10, U_Starlight = 11, U_Antidote = 12, U_Gale = 13,
            U_SoulFlame = 14, U_SoulBond = 15;   // 보스(리빙 아머 발로르) 전용

        static Affix[] St(AffixKind k, int v) { return new[] { new Affix { Kind = k, Value = v } }; }

        public static readonly UniqueDef[] Uniques =
        {
            new UniqueDef { Id = U_Bloodthirst,  Name = "피의 갈증",     KindMask = KindWeapon, On = () => Cfg.UBloodthirstOn, DescF = () => "근접 공격으로 입힌 피해의 " + Cfg.UBloodthirstPct + "%만큼 체력을 흡수한다" },
            new UniqueDef { Id = U_Executioner,  Name = "처형인의 결의", KindMask = KindWeapon, On = () => Cfg.UExecutionerOn, DescF = () => "체력이 " + Cfg.UExecutionerHp + "% 이하인 적에게 주는 피해 +" + Cfg.UExecutionerDmg + "%" },
            new UniqueDef { Id = U_Tempest,      Name = "폭풍의 춤",     KindMask = KindWeapon, On = () => Cfg.UTempestOn, DescF = () => "공격할 때 소모하는 스태미나 -" + Cfg.UTempestPct + "%" },
            new UniqueDef { Id = U_Reaper,       Name = "영혼 수확자",   KindMask = KindWeapon, On = () => Cfg.UReaperOn, DescF = () => "적을 처치하면 최대 체력의 " + Cfg.UReaperHp + "%, 최대 스태미나의 " + Cfg.UReaperSt + "%를 회복한다" },
            new UniqueDef { Id = U_Berserker,    Name = "광전사의 분노", KindMask = KindWeapon, On = () => Cfg.UBerserkerOn, DescF = () => "자신의 체력이 " + Cfg.UBerserkerHp + "% 이하일 때 주는 근접 피해 +" + Cfg.UBerserkerDmg + "%" },
            new UniqueDef { Id = U_Gale,         Name = "질풍의 손놀림", KindMask = KindWeapon, On = () => Cfg.UGaleOn, DescF = () => "공격 속도 +" + Cfg.UGalePct + "%" },
            new UniqueDef { Id = U_Undying,      Name = "불굴의 맹세",   KindMask = KindArmor,  On = () => Cfg.UUndyingOn, DescF = () => "자신의 체력이 " + Cfg.UUndyingHp + "% 이하일 때 받는 피해 -" + Cfg.UUndyingPct + "%" },
            new UniqueDef { Id = U_Regeneration, Name = "재생의 숨결",   KindMask = KindArmor,  On = () => Cfg.URegenOn, DescF = () => "매초 최대 체력의 " + Cfg.URegenPct.ToString("0.0##", CultureInfo.InvariantCulture) + "%를 회복한다" },
            new UniqueDef { Id = U_Shadow,       Name = "그림자 걸음",   KindMask = KindArmor,  On = () => Cfg.UShadowOn, DescF = () => "은신 +" + Cfg.UShadowStealth + "%, 회피율 +" + Cfg.UShadowDodge + "%",
                StatsF = () => new[] { new Affix { Kind = AffixKind.Stealth, Value = Cfg.UShadowStealth }, new Affix { Kind = AffixKind.Dodge, Value = Cfg.UShadowDodge } } },
            new UniqueDef { Id = U_Thorns,       Name = "가시 방패",     KindMask = KindShield, On = () => Cfg.UThornsOn, DescF = () => "방패로 막아낸 공격의 " + Cfg.UThornsPct + "%를 공격자에게 되돌려준다" },
            new UniqueDef { Id = U_Guardian,     Name = "수호자의 맹세", KindMask = KindShield, On = () => Cfg.UGuardianOn, DescF = () => "받는 모든 피해 -" + Cfg.UGuardianPct + "%" },
            new UniqueDef { Id = U_Starlight,    Name = "별빛의 가호",   KindMask = KindAny,    On = () => Cfg.UStarlightOn, DescF = () => "어둠 저항력 +" + Cfg.UStarlightPct + "%",
                StatsF = () => St(AffixKind.DarknessResist, Cfg.UStarlightPct) },
            new UniqueDef { Id = U_Antidote,     Name = "해독의 축복",   KindMask = KindAny,    On = () => Cfg.UAntidoteOn, DescF = () => "독 저항력 +" + Cfg.UAntidotePct + "%",
                StatsF = () => St(AffixKind.PoisonResist, Cfg.UAntidotePct) },
            new UniqueDef { Id = U_SoulFlame,    Name = "원혼의 불꽃",   KindMask = KindWeapon, On = () => true, BossOnly = true, ItemName = BossSwordName,
                DescF = () => "맞은 적을 푸른 원혼의 불꽃으로 태운다 (" + Cfg.USoulFlameSec + "초 동안 매초 최대 체력의 " + SoulFlamePctPerSec + "% 피해, 이동 속도 -" + Cfg.USoulFlameSlow + "%)" },
            new UniqueDef { Id = U_SoulBond,     Name = "원혼의 결속",   KindMask = KindArmor,  On = () => true, BossOnly = true, ItemName = BossArmorName,
                DescF = () => "원혼이 갑주를 붙들고 있어 내구도가 닳지 않는다" },
        };

        public const string BossSwordName = "발로르의 비가", BossArmorName = "발로르의 빈 갑주";

        static AffixDef A(AffixKind k, string name, bool pct, int min, int max, int mask, CharSpecType t)
        {
            return new AffixDef { Kind = k, Name = name, Percent = pct, Min = min, Max = max, KindMask = mask, Target = t };
        }

        public static UniqueDef GetUnique(int id)
        {
            for (int i = 0; i < Uniques.Length; i++) if (Uniques[i].Id == id) return Uniques[i];
            return null;
        }

        static AffixDef GetAffix(AffixKind k)
        {
            return Affixes[(int)k];
        }

        // ------------------------------------------------------------------ 판정
        static int ItemKind(HandItem h)
        {
            if (h is Weapon) return KindWeapon;
            if (h is Armor) return KindArmor;
            if (h is Shield) return KindShield;
            return 0;
        }

        static bool KindEnabled(int kind)
        {
            return (kind == KindWeapon && Cfg.ApplyWeapons) || (kind == KindArmor && Cfg.ApplyArmor) || (kind == KindShield && Cfg.ApplyShields);
        }

        static bool IsEligible(HandItem h)
        {
            if (!h || string.IsNullOrEmpty(h.ItemName)) return false;
            if (!KindEnabled(ItemKind(h))) return false;
            if (string.IsNullOrEmpty(h.gameObject.scene.name)) return false;   // 프리팹
            Weapon w = h as Weapon;
            if ((object)w != null)
            {
                if (w.SkillType == SkillType.UnarmedCombat || w.SkillType == SkillType.WeaponCrossbows) return false;
                if (w.IsBroken || w.IsBone || w.IsClaws) return false;
                return true;
            }
            Armor a = h as Armor;
            if ((object)a != null) return !a.NonUsable;
            return h is Shield;
        }

        static bool SpecsReady(HandItem h)
        {
            Weapon w = h as Weapon;
            if ((object)w != null) return w.GetDamage(false) > 0f;
            Armor a = h as Armor;
            if ((object)a != null) return a.GetDefense(false) > 0f;
            Shield s = h as Shield;
            if ((object)s != null) return s.GetDefense(false) > 0f;
            return false;
        }

        static bool CanRollHere(HandItem h)
        {
            // 살아있는 적이 들고 있는 장비는 굴리지 않는다 (시체/바닥/상자/영웅 소지품은 허용)
            if (!MainChar.Instance || !MainChar.Instance.Character) return false;
            CharacterBase owner = h.GetComponentInParent<CharacterBase>();
            if ((bool)owner && owner != MainChar.Instance.Character && !owner.IsDead) return false;
            return true;
        }

        public static ModData Get(HandItem h)
        {
            ModData d;
            if ((object)h != null && Data.TryGetValue(h, out d)) return d;
            return null;
        }

        public static ModData EnsureRolled(HandItem h)
        {
            ModData d = Get(h);
            if (d != null) return d;
            if (!IsEligible(h) || !CanRollHere(h) || !SpecsReady(h)) return null;
            d = Roll(h);
            Data[h] = d;
            return d;
        }

        // ------------------------------------------------------------------ 굴림
        static ModData Roll(HandItem h) { return Roll(h, null); }

        static ModData Roll(HandItem h, UniqueDef forced)
        {
            int kind = ItemKind(h);
            var d = new ModData();

            UniqueDef unique = forced;
            if (unique == null && Rng.NextDouble() * 100.0 < (_override ? _ovUnique : Cfg.UniqueChance))
            {
                var pool = new List<UniqueDef>();
                HashSet<int> blocked = BlockedUniques(h);
                foreach (var u in Uniques)
                    if ((u.KindMask & kind) != 0 && !u.BossOnly && u.On() && !blocked.Contains(u.Id)) pool.Add(u);
                if (pool.Count > 0) unique = pool[Rng.Next(pool.Count)];
            }

            if (unique != null)
            {
                d.Unique = unique.Id;
                d.Tier = 2;
            }
            else
            {
                int w0 = Math.Max(0, _override ? _ovW0 : Cfg.TierWeightLow), w1 = Math.Max(0, _override ? _ovW1 : Cfg.TierWeightMid), w2 = Math.Max(0, _override ? _ovW2 : Cfg.TierWeightHigh);
                int total = w0 + w1 + w2;
                if (total <= 0) d.Tier = 1;
                else
                {
                    int r = Rng.Next(total);
                    d.Tier = r < w0 ? 0 : (r < w0 + w1 ? 1 : 2);
                }
            }

            int cmin, cmax;
            if (d.Unique != 0) Cfg.Range(Cfg.OptUniqueMin, Cfg.OptUniqueMax, out cmin, out cmax);
            else if (d.Tier == 0) Cfg.Range(Cfg.OptLowMin, Cfg.OptLowMax, out cmin, out cmax);
            else if (d.Tier == 1) Cfg.Range(Cfg.OptMidMin, Cfg.OptMidMax, out cmin, out cmax);
            else Cfg.Range(Cfg.OptHighMin, Cfg.OptHighMax, out cmin, out cmax);
            int count = Rng.Next(cmin, cmax + 1);

            var candidates = new List<AffixDef>();
            foreach (var a in Affixes)
            {
                if ((a.KindMask & kind) == 0 || !Cfg.AffixOn(a.Kind)) continue;
                if (a.Kind == AffixKind.WeightReduction && h.Weight <= 0f) continue;
                candidates.Add(a);
            }
            for (int i = 0; i < count && candidates.Count > 0; i++)
            {
                int idx = Rng.Next(candidates.Count);
                AffixDef def = candidates[idx];
                candidates.RemoveAt(idx);
                d.Affixes.Add(new Affix { Kind = def.Kind, Value = RollValue(def, d.Tier) });
            }

            ApplyPermanentChanges(h, d);
            return d;
        }

        static int RollValue(AffixDef def, int tier)
        {
            int min, max;
            Cfg.AffixRange(def.Kind, def.Min, def.Max, out min, out max);
            if (!def.Percent)
            {
                // 능력치: 보통 최소값, 찬란한(유니크 포함)이면 설정 확률로 최대값
                return (tier == 2 && Rng.Next(100) < Cfg.AttrMaxChance) ? max : min;
            }
            double lo = tier == 0 ? 0.0 : (tier == 1 ? 0.2 : 0.5);
            double hi = tier == 0 ? 0.5 : (tier == 1 ? 0.8 : 1.0);
            double r = lo + Rng.NextDouble() * (hi - lo);
            int v = (int)Math.Round(min + (max - min) * r);
            return Math.Max(1, v);
        }

        static void ApplyPermanentChanges(HandItem h, ModData d)
        {
            foreach (var a in d.Affixes)
            {
                if (a.Kind == AffixKind.WeightReduction && h.Weight > 0f)
                {
                    float w = h.Weight * (1f - a.Value / 100f);
                    h.Weight = Mathf.Max(0.1f, Mathf.Round(w * 10f) / 10f);
                }
            }
        }

        static HashSet<int> BlockedUniques(HandItem except)
        {
            var set = new HashSet<int>();
            // 1) 영웅이 지니고 있는 유니크
            if (Cfg.BlockHeldUniques && (bool)MainChar.Instance && (bool)MainChar.Instance.Inventory)
            {
                foreach (var it in MainChar.Instance.Inventory.InventoryList)
                {
                    ModData d = Get(it);
                    if (d != null && d.Unique != 0) set.Add(d.Unique);
                }
            }
            // 2) 현재 레벨에 이미 나타나 있는 유니크 (중복 드랍 방지)
            if (Cfg.BlockFloorUniques) foreach (var kv in Data)
            {
                if (kv.Value.Unique == 0 || !kv.Key || kv.Key == except) continue;
                set.Add(kv.Value.Unique);
            }
            return set;
        }

        static void Cleanup()
        {
            List<HandItem> dead = null;
            foreach (var kv in Data)
                if (!kv.Key) (dead ?? (dead = new List<HandItem>())).Add(kv.Key);
            if (dead != null) foreach (var k in dead) Data.Remove(k);
        }

        // ------------------------------------------------------------------ 표시
        public static float TierMultiplier(HandItem h)
        {
            ModData d = Get(h);
            return d == null ? 1f : Mathf.Max(0f, 1f + TierPowerPct(d) / 100f);
        }

        static string AffixText(AffixKind kind, int value)
        {
            AffixDef def = GetAffix(kind);
            return def.Name + (def.Negative ? " -" : " +") + value + (def.Percent ? "%" : "");
        }

        static Color TierColor(ModData d)
        {
            var ui = UserInterface.Instance;
            if (!ui) return Color.white;
            if (d.Unique != 0) return ui.HighlightColor;
            if (d.Tier == 2) return ui.PositiveColor;
            if (d.Tier == 0) return ui.DarkColor;
            return ui.WhiteColor;
        }

        static string Colored(string text, Color c)
        {
            try { return UIHelper.GetColoredText(text, c); }
            catch { return text; }
        }

        // HandItem.GetLocalizedName() 후처리
        [Hook(HookKind.Postfix, "HandItems.HandItem", "GetLocalizedName", Params = 0)]
        public static string DecorateName(string result, HandItem h)
        {
            long perf = Perf.Begin();
            try
            {
                ModData d = EnsureRolled(h);
                if (d == null || string.IsNullOrEmpty(result)) return result;
                if (d.Unique != 0)
                {
                    UniqueDef u = GetUnique(d.Unique);
                    if (u != null && u.ItemName != null) return u.ItemName;
                    if (u != null) return "《" + u.Name + "》 " + result;
                }
                return "[" + TierName[d.Tier] + "] " + result;
            }
            catch (Exception e) { LogOnce(e); return result; }
            finally { Perf.End("Modifiers 아이템 이름 표시", perf); }
        }

        // HandItem.GetFeaturesText() 후처리 - 아이템 설명 패널에 옵션을 표시
        [Hook(HookKind.Postfix, "HandItems.HandItem", "GetFeaturesText", Params = 0)]
        public static string FeaturesText(string result, HandItem h)
        {
            try
            {
                ModData d = EnsureRolled(h);
                if (d == null) return result;
                var ui = UserInterface.Instance;
                var lines = new List<string>();
                int pct = TierPowerPct(d);
                string tierLine = "품질: " + (d.Unique != 0 ? "유니크" : TierName[d.Tier]) + (pct == 0 ? "" : " (기본 성능 " + (pct > 0 ? "+" : "") + pct + "%)");
                lines.Add(Colored(tierLine, TierColor(d)));
                foreach (var a in d.Affixes)
                    lines.Add(Colored("◆ " + AffixText(a.Kind, a.Value), ui ? ui.PositiveColor : Color.green));
                if (d.Unique != 0)
                {
                    UniqueDef u = GetUnique(d.Unique);
                    if (u != null)
                        lines.Add(Colored("★ 유니크 【" + u.Name + "】 " + u.Desc, ui ? ui.HighlightColor : Color.yellow));
                }
                string mine = string.Join("\n", lines.ToArray());
                return string.IsNullOrEmpty(result) ? mine : result + "\n" + mine;
            }
            catch (Exception e) { LogOnce(e); return result; }
        }

        // ------------------------------------------------------------------ 기본 성능 배율
        [Hook(HookKind.Postfix, "HandItems.Weapon", "GetDamage", Params = 1)]
        public static float ScaleWeaponDamage(float result, Weapon w) { return result * TierMultiplier(w); }
        [Hook(HookKind.Postfix, "HandItems.Weapon", "GetDefense", Params = 1)]
        public static float ScaleWeaponDefense(float result, Weapon w) { return result * TierMultiplier(w); }
        [Hook(HookKind.Postfix, "HandItems.Armor", "GetDefense", Params = 1)]
        [Hook(HookKind.Postfix, "HandItems.Armor", "GetDefenseWithBuffs", Params = 1)]
        public static float ScaleArmorDefense(float result, Armor a) { return result * TierMultiplier(a); }
        [Hook(HookKind.Postfix, "HandItems.Shield", "GetDefense", Params = 1)]
        public static float ScaleShieldDefense(float result, Shield s) { return result * TierMultiplier(s); }

        // ------------------------------------------------------------------ 버프(능력치) 연동
        // Buff.GetBuffImpactValue(BuffStrength) 앞부분: NaN 이 아니면 그 값을 그대로 반환
        [Hook(HookKind.PrefixReturn, "Buff", "GetBuffImpactValue", Params = 1, Args = new[] { 0 })]
        public static float BuffImpact(Buff b)
        {
            BuffInfo info = GetBuffInfo(b);
            return info == null ? float.NaN : info.Value;
        }

        // Buff.GetBuffEffectDesc(BuffStrength, bool) 앞부분: null 이 아니면 그 문자열 반환
        [Hook(HookKind.PrefixReturn, "Buff", "GetBuffEffectDesc", Params = 2, Args = new[] { 0 })]
        public static string BuffDesc(Buff b)
        {
            BuffInfo info = GetBuffInfo(b);
            return info == null ? null : info.Desc;
        }

        static BuffInfo GetBuffInfo(Buff b)
        {
            if ((object)b == null) return null;
            string name = b.Name;
            if (name == null || !name.StartsWith(BuffPrefix, StringComparison.Ordinal)) return null;
            BuffInfo info;
            if (BuffCache.TryGetValue(name, out info)) return info;
            info = new BuffInfo { Value = 0f, Desc = string.Empty };
            string[] p = name.Split('|');
            try
            {
                if (p.Length >= 4 && (p[1] == "A" || p[1] == "S"))
                {
                    var kind = (AffixKind)int.Parse(p[2], CultureInfo.InvariantCulture);
                    int v = int.Parse(p[3], CultureInfo.InvariantCulture);
                    info.Value = v;
                    info.Desc = AffixText(kind, v);
                }
                else if (p.Length == 2 && p[1] == "I")
                {
                    return new BuffInfo { Value = 0f, Desc = ItemSummary(b.ItemReference) };
                }
                else if (p.Length >= 3 && p[1] == "U")
                {
                    UniqueDef u = GetUnique(int.Parse(p[2], CultureInfo.InvariantCulture));
                    if (u != null) return new BuffInfo { Value = 0f, Desc = "【" + u.Name + "】 " + u.Desc };
                }
            }
            catch { }
            BuffCache[name] = info;
            return info;
        }

        // 캐릭터 정보창용: 장비 하나의 옵션을 한 줄로 합친 설명
        static string ItemSummary(HandItem h)
        {
            ModData d = Get(h);
            if (d == null) return string.Empty;
            var parts = new List<string>();
            foreach (var a in d.Affixes) parts.Add(AffixText(a.Kind, a.Value));
            if (d.Unique != 0)
            {
                UniqueDef u = GetUnique(d.Unique);
                if (u != null)
                {
                    parts.Add("★" + u.Name);
                    foreach (var a in u.Stats) parts.Add(AffixText(a.Kind, a.Value));
                }
            }
            return string.Join(", ", parts.ToArray());
        }

        // MainCharPanel.ShowBuffs 뒤: 장비 옵션 하나하나(A/S/U)는 숨기고, 장비별 요약(I)만 보이게 한다.
        // (예전 방식은 목록을 바꿔치기했는데, 게임 코드 안의 람다는 원래 목록을 봐서 같은 칸을 매번 새로 만드는 문제가 있었음)
        [Hook(HookKind.Postfix, "UI.Dialogs.MainCharPanel", "ShowBuffs", Params = 0, Args = new[] { 0 })]
        public static void AfterShowBuffs(UI.Dialogs.MainCharPanel p)
        {
            try
            {
                if (!p || !p.BuffPanel || !MainChar.Instance || MainChar.Instance.Character == null) return;
                var hide = new HashSet<int>();
                foreach (var b in MainChar.Instance.Character.Buffs)
                {
                    if (!b || b.Name == null || !b.Name.StartsWith(BuffPrefix, StringComparison.Ordinal)) continue;
                    if (b.Name.Equals(BuffPrefix + "I", StringComparison.Ordinal)) continue;
                    hide.Add(b.GetInstanceID());
                }
                var seen = new HashSet<int>();
                foreach (var u in p.BuffPanel.GetComponentsInChildren<UsableItem>(true))
                {
                    if (!u || u.IsSpecial || u.Id == 0) continue;
                    if (!seen.Add(u.Id)) { UnityEngine.Object.Destroy(u.gameObject); continue; }   // 예전 문제로 겹쳐 생긴 칸 정리
                    if (!hide.Contains(u.Id) || u.transform.localScale == Vector3.zero) continue;
                    // 끄면 게임이 '없는 칸'으로 보고 다시 만들기 때문에, 켜 둔 채로 크기만 0 으로
                    u.transform.localScale = Vector3.zero;
                    var le = u.GetComponent<LayoutElement>() ?? u.gameObject.AddComponent<LayoutElement>();
                    le.ignoreLayout = true;
                }
            }
            catch (Exception e) { LogOnce(e); }
        }

        // MainCharPanel.ShowSpecs 뒤: 독/어둠 저항력을 능력치 표의 빈칸(장비 무게 옆)으로 옮긴다
        static Text _resLabel, _resValue;
        static UI.Dialogs.MainCharPanel _resPanel;
        [Hook(HookKind.Postfix, "UI.Dialogs.MainCharPanel", "ShowSpecs", Params = 0, Args = new[] { 0 })]
        public static void AfterShowSpecs(UI.Dialogs.MainCharPanel p)
        {
            try
            {
                if (!p || !p.DefenseItemLabel || !p.DefenseItem || !p.EquipmentWeightLabel || !p.EquipmentWeight) return;
                if (_resPanel != p || !_resLabel || !_resValue)
                {
                    _resPanel = p;
                    _resLabel = CloneText(p.DefenseItemLabel, "ModResistLabel");
                    _resValue = CloneText(p.DefenseItem, "ModResistValue");
                    Vector3 lp = p.DefenseItemLabel.transform.position, vp = p.DefenseItem.transform.position;
                    _resLabel.transform.position = new Vector3(lp.x, p.EquipmentWeightLabel.transform.position.y, lp.z);
                    _resValue.transform.position = new Vector3(vp.x, p.EquipmentWeight.transform.position.y, vp.z);
                    _resValue.horizontalOverflow = HorizontalWrapMode.Overflow;
                    _resLabel.horizontalOverflow = HorizontalWrapMode.Overflow;
                }
                if (p.PoisonResistanceLabel) p.PoisonResistanceLabel.gameObject.SetActive(false);
                if (p.DarknessResistanceLabel) p.DarknessResistanceLabel.gameObject.SetActive(false);
                CharacterBase c = MainChar.Instance.Character;
                float poison = c.GetPoisonResistance(), dark = c.GetDarknessResistance();
                var ui = UserInterface.Instance;
                Color neutral = p.EquipmentWeightLabel.color;
                Color col = (poison > 0f || dark > 0f) && ui ? ui.PositiveColor : neutral;
                string label = "저항력", value = "독 " + Mathf.RoundToInt(poison) + "%  ·  어둠 " + Mathf.RoundToInt(dark) + "%";
                if (_resLabel.text != label) _resLabel.text = label;
                if (_resValue.text != value) _resValue.text = value;
                _resLabel.color = col;
                _resValue.color = col;
            }
            catch (Exception e) { LogOnce(e); }
        }

        static Text CloneText(Text src, string name)
        {
            GameObject go = UnityEngine.Object.Instantiate(src.gameObject, src.transform.parent, true);
            go.name = name;
            foreach (var c in go.GetComponentsInChildren<MonoBehaviour>(true))
                if (c && !(c is Text) && c.GetType().Name == "LocalizedText") UnityEngine.Object.DestroyImmediate(c);
            go.SetActive(true);
            return go.GetComponent<Text>();
        }

        static Buff CreateModBuff(string name, HandItem item, Transform parent, CharSpecType target, BuffSpecialEffect effect)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            Buff b = go.AddComponent<Buff>();
            b.Name = name;
            b.Target = target;
            b.SpecialEffect = effect;
            b.IsPositive = true;
            b.Duration = BuffDuration.Permanent;
            b.BuffImpactType = BuffImpactType.FixedValue;
            b.Strength = BuffStrength.Low;
            b.HideInBuffPanel = true;
            b.IsKnown = true;
            b.ItemReference = item;
            return b;
        }

        struct Wanted
        {
            public HandItem Item;
            public string Name;
            public CharSpecType Target;
            public BuffSpecialEffect Effect;
        }

        static void AddWantedStat(List<Wanted> list, HandItem item, string tag, Affix a)
        {
            AffixDef def = GetAffix(a.Kind);
            if (def.Target == CharSpecType.None && def.Effect == BuffSpecialEffect.None) return; // 아이템 자체 변경(무게)
            list.Add(new Wanted
            {
                Item = item,
                Name = BuffPrefix + tag + "|" + (int)a.Kind + "|" + a.Value,
                Target = def.Target,
                Effect = def.Effect
            });
        }

        // CharacterBase.CalculateSpecs() 앞부분 (0.2초마다 호출)
        [Hook(HookKind.Prefix, "CharacterBase", "CalculateSpecs", Params = 0, Args = new[] { 0 })]
        public static void Tick(CharacterBase ch)
        {
            if (!MainChar.Instance || ch != MainChar.Instance.Character) return;
            long perf = Perf.Begin();
            try
            {
                MainChar mc = MainChar.Instance;
                if (!mc || ch != mc.Character || Game.IsPaused) return;
                if (Time.unscaledTime > _nextCleanup)
                {
                    _nextCleanup = Time.unscaledTime + 15f;
                    Cleanup();
                }
                SyncHero(mc, ch);
                TickFlames();
                if ((_activeUniques & (1 << U_Regeneration)) != 0 && !ch.IsDead)
                {
                    CharSpec hp = ch.GetSpec(CharSpecType.Health);
                    if (hp != null && hp.CurrentValue > 0f && hp.CurrentValue < hp.MaxValue)
                        hp.CurrentValue = Mathf.Min(hp.MaxValue, hp.CurrentValue + hp.MaxValue * (Cfg.URegenPct / 100f) * 0.2f);
                }
            }
            catch (Exception e) { LogOnce(e); }
            finally { Perf.End("Modifiers 장비 옵션 적용(0.2초마다)", perf); }
        }

        static void SyncHero(MainChar mc, CharacterBase ch)
        {
            Inventory inv = mc.Inventory;
            if (!inv || ch.Buffs == null) return;
            var wanted = new List<Wanted>();
            int mask = 0;
            for (int i = 0; i < inv.InventoryList.Count; i++)
            {
                HandItem it = inv.InventoryList[i];
                if (!it) continue;
                ModData d = EnsureRolled(it);
                if (d == null || !it.IsEquipped) continue;
                if (d.Affixes.Count > 0 || d.Unique != 0)
                    wanted.Add(new Wanted { Item = it, Name = BuffPrefix + "I", Target = CharSpecType.None, Effect = BuffSpecialEffect.None });
                foreach (var a in d.Affixes) AddWantedStat(wanted, it, "A", a);
                if (d.Unique != 0)
                {
                    UniqueDef u = GetUnique(d.Unique);
                    if (u == null) continue;
                    mask |= 1 << u.Id;
                    wanted.Add(new Wanted { Item = it, Name = BuffPrefix + "U|" + u.Id, Target = CharSpecType.None, Effect = BuffSpecialEffect.None });
                    foreach (var a in u.Stats) AddWantedStat(wanted, it, "S", a);
                }
            }
            _activeUniques = mask;

            List<Buff> buffs = ch.Buffs;
            for (int i = buffs.Count - 1; i >= 0; i--)
            {
                Buff b = buffs[i];
                if (!b || b.Name == null || !b.Name.StartsWith(BuffPrefix, StringComparison.Ordinal)) continue;
                bool keep = false;
                for (int j = 0; j < wanted.Count; j++)
                {
                    if (wanted[j].Name == b.Name && wanted[j].Item == b.ItemReference) { keep = true; break; }
                }
                if (!keep)
                {
                    buffs.RemoveAt(i);
                    UnityEngine.Object.Destroy(b.gameObject);
                }
            }
            for (int j = 0; j < wanted.Count; j++)
            {
                bool have = false;
                for (int i = 0; i < buffs.Count; i++)
                {
                    Buff b = buffs[i];
                    if ((bool)b && b.Name == wanted[j].Name && b.ItemReference == wanted[j].Item) { have = true; break; }
                }
                if (!have)
                    buffs.Add(CreateModBuff(wanted[j].Name, wanted[j].Item, ch.transform, wanted[j].Target, wanted[j].Effect));
            }
        }

        static bool HeroHas(int uniqueId)
        {
            return (_activeUniques & (1 << uniqueId)) != 0;
        }

        static bool ItemHas(HandItem h, int uniqueId)
        {
            ModData d = Get(h);
            return d != null && d.Unique == uniqueId && h.IsEquipped;
        }

        static bool IsHero(CharacterBase c)
        {
            return (bool)c && (bool)MainChar.Instance && c == MainChar.Instance.Character;
        }

        // ------------------------------------------------------------------ 전투 훅
        // CharacterBase.ReduceHealth(float) 앞부분: 영웅이 받는 피해 감소
        [Hook(HookKind.PrefixArg, "CharacterBase", "ReduceHealth", Params = 1, ArgIndex = 1, Args = new[] { 0, 1 })]
        public static float ModifyIncoming(CharacterBase self, float dmg)
        {
            try
            {
                if (dmg <= 0f || _activeUniques == 0 || !IsHero(self)) return dmg;
                if (HeroHas(U_Guardian)) dmg *= 1f - Cfg.UGuardianPct / 100f;
                if (HeroHas(U_Undying) && self.GetHealthPercent() <= Cfg.UUndyingHp) dmg *= 1f - Cfg.UUndyingPct / 100f;
            }
            catch (Exception e) { LogOnce(e); }
            return dmg;
        }

        // CharacterBase.Damaged(근접) 내부의 ReduceHealth 호출을 대체
        [Hook(HookKind.ReplaceCall, "CharacterBase", "Damaged", ParamTypes = MeleeDamaged, CallType = "CharacterBase", CallMethod = "ReduceHealth", CallParams = 1, Args = new[] { 1, 2 })]
        public static float MeleeReduceHealth(CharacterBase victim, float dmg, CharacterBase attacker, Weapon weapon)
        {
            bool heroAttack = false;
            float before = 0f;
            try
            {
                heroAttack = IsHero(attacker) && (bool)weapon && !IsHero(victim);
                if (heroAttack)
                {
                    if (ItemHas(weapon, U_Executioner) && victim.GetHealthPercent() <= Cfg.UExecutionerHp) dmg *= 1f + Cfg.UExecutionerDmg / 100f;
                    if (ItemHas(weapon, U_Berserker) && attacker.GetHealthPercent() <= Cfg.UBerserkerHp) dmg *= 1f + Cfg.UBerserkerDmg / 100f;
                    CharSpec vhp = victim.GetSpec(CharSpecType.Health);
                    before = vhp != null ? vhp.CurrentValue : 0f;
                }
            }
            catch (Exception e) { LogOnce(e); heroAttack = false; }

            float remain = victim.ReduceHealth(dmg);

            try
            {
                if (heroAttack && ItemHas(weapon, U_Bloodthirst))
                {
                    float dealt = Mathf.Clamp(before - remain, 0f, dmg);
                    CharSpec hp = attacker.GetSpec(CharSpecType.Health);
                    if (hp != null && hp.CurrentValue > 0f)
                        hp.CurrentValue = Mathf.Min(hp.MaxValue, hp.CurrentValue + dealt * (Cfg.UBloodthirstPct / 100f));
                }
            }
            catch (Exception e) { LogOnce(e); }
            return remain;
        }

        // CharacterBase.Damaged(근접) 시작 부분: 가시 방패
        [Hook(HookKind.Prefix, "CharacterBase", "Damaged", ParamTypes = MeleeDamaged, Args = new[] { 0, 1, 2, 3, 6 })]
        public static void OnMeleeHit(CharacterBase victim, CharacterBase attacker, Weapon weapon, EnemySector sector, HandItem defenceItem)
        {
            try
            {
                if (!IsHero(victim) || !attacker || !weapon || IsHero(attacker)) return;
                if (sector != EnemySector.FrontSector || !victim.IsDefensePosition || !defenceItem) return;
                if (!(defenceItem is Shield) || !ItemHas(defenceItem, U_Thorns)) return;
                float reflect = attacker.GetWeaponDamage(weapon) * (Cfg.UThornsPct / 100f);
                CharSpec ehp = attacker.GetSpec(CharSpecType.Health);
                if (ehp == null || ehp.CurrentValue <= 1f || reflect <= 0f) return;
                attacker.ReduceHealth(Mathf.Min(reflect, ehp.CurrentValue - 1f));
            }
            catch (Exception e) { LogOnce(e); }
        }

        // MainChar.UpdateAnimatorSpeed() 뒷부분 (0.1초마다): 공격 중이면 애니메이션 속도를 올린다
        static System.Reflection.FieldInfo _animatorField;
        [Hook(HookKind.Postfix, "MainChar", "UpdateAnimatorSpeed", Params = 0, Args = new[] { 0 })]
        public static void AttackSpeed(MainChar mc)
        {
            try
            {
                if (!HeroHas(U_Gale) || Game.IsPaused || !mc || !mc.Character || mc.Character.IsDead) return;
                if (_animatorField == null) _animatorField = typeof(MainChar).GetField("_animator", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                Animator anim = _animatorField == null ? null : _animatorField.GetValue(mc) as Animator;
                if (!anim || !anim.GetBool("IsAttacking") || anim.GetLayerWeight(5) > 0f) return;
                Weapon w = mc.GetRightHandWeapon();
                Weapon lw = mc.GetLeftHandWeapon();
                if (!ItemHas(w, U_Gale) && !ItemHas(lw, U_Gale)) return;
                anim.speed *= 1f + Cfg.UGalePct / 100f;
            }
            catch (Exception e) { LogOnce(e); }
        }

        // CharacterBase.ReduceStaminaAttack(Weapon) 앞부분: true 면 원본 건너뜀
        [Hook(HookKind.PrefixSkip, "CharacterBase", "ReduceStaminaAttack", Params = 1, Args = new[] { 0, 1 })]
        public static bool StaminaAttack(CharacterBase self, Weapon weapon)
        {
            try
            {
                if (!weapon || !IsHero(self) || !ItemHas(weapon, U_Tempest)) return false;
                CharSpec spec = self.GetSpec(CharSpecType.Stamina);
                float num = 3f + weapon.Weight;
                if (weapon.AttackMultiplier > 0f) num *= weapon.AttackMultiplier;
                num *= 1f - Cfg.UTempestPct / 100f;
                spec.CurrentValue = num > spec.CurrentValue ? 0f : spec.CurrentValue - num;
                self.AddTiredness(num);
                return true;
            }
            catch (Exception e) { LogOnce(e); return false; }
        }

        // BestHero.AddKilledCreature(CharacterBase, CreatureDeathType) 앞부분
        [Hook(HookKind.Prefix, "Management.BestHero", "AddKilledCreature", Params = 2, Args = new[] { 1, 2 })]
        public static void OnKill(CharacterBase killed, CreatureDeathType type)
        {
            try
            {
                if (!HeroHas(U_Reaper) || !MainChar.Instance) return;
                CharacterBase hero = MainChar.Instance.Character;
                if (!hero || hero.IsDead) return;
                CharSpec hp = hero.GetSpec(CharSpecType.Health);
                CharSpec st = hero.GetSpec(CharSpecType.Stamina);
                if (hp != null && hp.CurrentValue > 0f) hp.CurrentValue = Mathf.Min(hp.MaxValue, hp.CurrentValue + hp.MaxValue * (Cfg.UReaperHp / 100f));
                if (st != null) st.CurrentValue = Mathf.Min(st.MaxValue, st.CurrentValue + st.MaxValue * (Cfg.UReaperSt / 100f));
            }
            catch (Exception e) { LogOnce(e); }
        }

        // ------------------------------------------------------------------ 저장 / 불러오기 (로더 공용 저장 기능 사용)
        // 저장 키 이름(SpkMod)은 예전 세이브와의 호환을 위해 그대로 쓴다.
        [ModInit]
        public static void Init()
        {
            SaveExt.Register("SpkMod",
                o =>
                {
                    HandItem h = o as HandItem;
                    if ((object)h == null) return null;
                    ModData d = Get(h);
                    return d == null ? null : d.Serialize();
                },
                (o, v) =>
                {
                    HandItem h = o as HandItem;
                    if ((object)h == null) return;
                    ModData d = ModData.Parse(v);
                    if (d != null) Data[h] = d;
                    else Data.Remove(h);
                });
        }

        // ------------------------------------------------------------------ 보스 전용 장비 (보스 모드가 부름)
        // 정해진 유니크 능력과 일반 유니크와 같은 개수의 추가 옵션을 붙인다.
        public static bool MakeBossUnique(HandItem h, int uniqueId)
        {
            try
            {
                UniqueDef u = GetUnique(uniqueId);
                if (!h || u == null) return false;
                Data[h] = Roll(h, u);
                return true;
            }
            catch (Exception e) { LogOnce(e); return false; }
        }

        // 원혼의 결속: 이 갑옷은 내구도가 닳지 않는다 (HandItem.Damaged 앞부분, true 면 원본 건너뜀)
        [Hook(HookKind.PrefixSkip, "HandItems.HandItem", "Damaged", Params = 3, Args = new[] { 0 })]
        public static bool NoWear(HandItem h)
        {
            ModData d = Get(h);
            return d != null && d.Unique == U_SoulBond;
        }

        // 원혼의 불꽃: 이 무기로 몬스터를 베면 몸에 푸른 불꽃이 붙어 타오른다 (지속 피해 + 둔화)
        //  불꽃 모양은 휴식처 화톳불의 불 효과를 복사해 푸르게 바꾼 것
        class Flame { public float Until, Last; public GameObject Fx; public float Move = -1f, Run = -1f; public bool Logged; }
        static readonly Dictionary<Creature, Flame> Flames = new Dictionary<Creature, Flame>();
        public const float SoulFlamePctPerSec = 1.8f;   // 최대 체력의 %/초 (게임의 강한 출혈은 1.5%/초)

        [Hook(HookKind.Postfix, "Creatures.Creature", "Damaged", ParamTypes = "CharacterBase,Weapon,AttackDirection", Args = new[] { 0, 1, 2 })]
        public static DamageType FlameAfter(DamageType result, Creature c, CharacterBase attacker, Weapon w)
        {
            try
            {
                if (result == DamageType.None || !c || c.IsDead || !IsHero(attacker) || !ItemHas(w, U_SoulFlame)) return result;
                Flame f;
                if (!Flames.TryGetValue(c, out f)) { f = new Flame { Last = Time.time }; Flames[c] = f; SetFlameSlow(c, f, true); }
                f.Until = Time.time + Cfg.USoulFlameSec;
                if (!f.Fx)
                {
                    Transform at = c.BloodPoint ? c.BloodPoint : c.transform;
                    f.Fx = BlueFire.Attach(at, 0.55f);
                }
                try { EffectsManager.SpawnPointLightFlash(c.transform.position + Vector3.up * 1.2f, BlueFire.Color, 2.2f, 0.05f, 0.6f, 2.5f, true, true, 0.05f); } catch { }
            }
            catch (Exception e) { LogOnce(e); }
            return result;
        }

        static void SetFlameSlow(Creature c, Flame f, bool on)
        {
            Animator a = c.GetComponent<Animator>();
            if (!a) return;
            float k = 1f - Cfg.USoulFlameSlow / 100f;
            foreach (var p in a.parameters)
            {
                if (p.type != AnimatorControllerParameterType.Float) continue;
                if (p.name == "MoveSpeed")
                {
                    if (on) { f.Move = a.GetFloat(p.nameHash); a.SetFloat(p.nameHash, f.Move * k); }
                    else if (f.Move >= 0f) a.SetFloat(p.nameHash, f.Move);
                }
                else if (p.name == "RunSpeed")
                {
                    if (on) { f.Run = a.GetFloat(p.nameHash); a.SetFloat(p.nameHash, f.Run * k); }
                    else if (f.Run >= 0f) a.SetFloat(p.nameHash, f.Run);
                }
            }
        }

        static void EndFlame(Creature c, Flame f)
        {
            Flames.Remove(c);
            if (f.Fx) BlueFire.Fade(f.Fx);
            if (c && !c.IsDead) SetFlameSlow(c, f, false);
        }

        // 영웅 쪽 0.2초 주기에서 불린다: 불꽃 피해를 주고, 끝난 불꽃은 끈다
        static void TickFlames()
        {
            if (Flames.Count == 0) return;
            var list = new List<KeyValuePair<Creature, Flame>>(Flames);
            foreach (var kv in list)
            {
                Creature c = kv.Key; Flame f = kv.Value;
                if (!c || c.IsDead || f.Until <= Time.time) { EndFlame(c, f); continue; }
                float dt = Mathf.Clamp(Time.time - f.Last, 0f, 1f);
                f.Last = Time.time;
                CharacterBase ch = c.Character;
                CharSpec hp = ch ? ch.GetSpec(CharSpecType.Health) : null;
                if (hp == null || hp.MaxValue <= 0f || dt <= 0f) continue;
                float dmg = hp.MaxValue * SoulFlamePctPerSec / 100f * dt;
                float before = hp.CurrentValue;
                ch.ReduceHealth(dmg);          // 다른 모드의 체력 배율(보스·접두사 몬스터)도 똑같이 적용된다
                if (!f.Logged) { f.Logged = true; Debug.Log("[Modifiers] 원혼의 불꽃: " + ch.CharacterName + " 체력 " + before.ToString("0.0") + " → " + hp.CurrentValue.ToString("0.0")); LogFlame(ch, before, hp.CurrentValue, hp.MaxValue); }
                if (hp.CurrentValue <= 0f && !c.IsDead)
                {
                    try
                    {
                        Necropolis.Instance.CurrentHero.AddKilledCreature(ch, CreatureDeathType.Injures);
                        Bestiary.Instance.IncrementKilledCount(ch.CharacterName);
                    }
                    catch { }
                    c.Death(true);
                    EndFlame(c, f);
                }
            }
        }

        static int _flameLogs;
        static void LogFlame(CharacterBase ch, float before, float after, float max)
        {
            if (_flameLogs++ >= 10) return;
            try { ModLoader.Loader.Log("[Modifiers] 원혼의 불꽃 피해 확인: " + ch.CharacterName + " 체력 " + before.ToString("0.0") + " → " + after.ToString("0.0") + " (최대 " + max.ToString("0") + ", 초당 " + SoulFlamePctPerSec + "%)"); } catch { }
        }

        // ------------------------------------------------------------------ 다른 모드용: 지정한 확률로 바로 등급을 정한다
        // (MobModifiers 의 보상 장비 등). 이미 등급이 있으면 그대로 둔다. 적용되면 true.
        public static bool ForceRoll(HandItem h, int weightLow, int weightMid, int weightHigh, float uniqueChance)
        {
            try
            {
                if (Get(h) != null) return true;
                if (!IsEligible(h) || !SpecsReady(h)) return false;
                _override = true;
                _ovW0 = weightLow; _ovW1 = weightMid; _ovW2 = weightHigh; _ovUnique = uniqueChance;
                Data[h] = Roll(h);
                return true;
            }
            catch (Exception e) { LogOnce(e); return false; }
            finally { _override = false; }
        }
        static bool _override;
        static int _ovW0, _ovW1, _ovW2;
        static float _ovUnique;

        static int _logged;
        static void LogOnce(Exception e)
        {
            if (_logged++ < 20) Debug.LogWarning("[Modifiers] " + e);
        }
    }

    // 휴식처 화톳불의 불 효과를 복사해 푸른 원혼의 불꽃으로 쓴다
    public static class BlueFire
    {
        public static readonly Color Color = new Color(0.35f, 0.65f, 1f, 1f);
        static GameObject _tpl;

        static GameObject Template()
        {
            if (_tpl) return _tpl;
            try
            {
                foreach (var sp in UnityEngine.Object.FindObjectsOfType<Actions.SleepingPlace>(true))
                {
                    if (!sp || !sp.BonfireActive || !sp.BonfireActive.Fire) continue;
                    var src = sp.BonfireActive.Fire.gameObject;
                    _tpl = UnityEngine.Object.Instantiate(src);
                    _tpl.name = "SoulFireTemplate";
                    _tpl.SetActive(false);
                    UnityEngine.Object.DontDestroyOnLoad(_tpl);
                    foreach (var mb in _tpl.GetComponentsInChildren<MonoBehaviour>(true)) UnityEngine.Object.DestroyImmediate(mb);
                    foreach (var au in _tpl.GetComponentsInChildren<AudioSource>(true)) UnityEngine.Object.DestroyImmediate(au);
                    Recolor(_tpl);
                    break;
                }
            }
            catch (Exception e) { Debug.LogWarning("[Modifiers] 푸른 불꽃 준비 실패: " + e.Message); }
            return _tpl;
        }

        static void Recolor(GameObject go)
        {
            foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true))
            {
                var main = ps.main;
                main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.55f, 0.85f, 1f, 1f), new Color(0.15f, 0.40f, 1f, 1f));
                main.scalingMode = ParticleSystemScalingMode.Hierarchy;
                var col = ps.colorOverLifetime;
                if (col.enabled)
                {
                    var g = new Gradient();
                    g.SetKeys(new[] { new GradientColorKey(new Color(0.75f, 0.92f, 1f), 0f), new GradientColorKey(new Color(0.25f, 0.55f, 1f), 0.5f), new GradientColorKey(new Color(0.05f, 0.15f, 0.6f), 1f) },
                              new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.12f), new GradientAlphaKey(0.8f, 0.6f), new GradientAlphaKey(0f, 1f) });
                    col.color = new ParticleSystem.MinMaxGradient(g);
                }
                var r = ps.GetComponent<Renderer>();
                if (r)
                {
                    var mats = r.materials;
                    foreach (var m in mats)
                    {
                        if (!m) continue;
                        if (m.HasProperty("_TintColor")) m.SetColor("_TintColor", new Color(0.35f, 0.6f, 1f, 0.6f));
                        if (m.HasProperty("_Color")) m.SetColor("_Color", new Color(0.45f, 0.75f, 1f, m.GetColor("_Color").a));
                        if (m.HasProperty("_EmissionColor")) m.SetColor("_EmissionColor", new Color(0.2f, 0.5f, 1.4f));
                    }
                    r.materials = mats;
                }
            }
            foreach (var l in go.GetComponentsInChildren<Light>(true)) l.color = Color;
        }

        // parent 에 붙인다 (scale: 화톳불 크기 대비)
        public static GameObject Attach(Transform parent, float scale)
        {
            var tpl = Template();
            GameObject go;
            if (tpl)
            {
                go = UnityEngine.Object.Instantiate(tpl, parent, false);
                go.name = "SoulFire";
                go.transform.localPosition = Vector3.zero;
                go.transform.localRotation = Quaternion.identity;
                Vector3 ps = parent.lossyScale;
                float inv = ps.x > 0.01f ? 1f / ps.x : 1f;
                go.transform.localScale = Vector3.one * scale * inv;
                go.SetActive(true);
                foreach (var p in go.GetComponentsInChildren<ParticleSystem>(true)) p.Play();
            }
            else
            {
                go = new GameObject("SoulFire");
                go.transform.SetParent(parent, false);
            }
            var lg = new GameObject("SoulFireLight");
            lg.transform.SetParent(go.transform, false);
            var l = lg.AddComponent<Light>();
            l.type = LightType.Point; l.color = Color; l.range = 3f; l.intensity = 1.6f; l.shadows = LightShadows.None;
            return go;
        }

        public static void Fade(GameObject go)
        {
            if (!go) return;
            foreach (var p in go.GetComponentsInChildren<ParticleSystem>(true)) p.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            foreach (var l in go.GetComponentsInChildren<Light>(true)) l.enabled = false;
            UnityEngine.Object.Destroy(go, 2.5f);
        }
    }
}
