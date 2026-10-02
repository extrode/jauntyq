namespace Extrode.JauntyQ.Analysis.Migrations;

/// <summary>
/// The order migration files apply in, shared by the generator's in-memory
/// simulation and <c>jauntyq migrate status</c> so the two cannot disagree
/// about which migration comes first. Documented for consumer runners in
/// <c>docs/06-reference/migration-tracking-contract.md</c>.
/// </summary>
public static class MigrationOrder
{
    /// <summary>
    /// Natural-order string comparison: runs of digits compare by numeric
    /// value, everything else compares ordinally. A plain ordinal sort (the
    /// old behavior) misorders common unpadded migration-file naming
    /// conventions -- Flyway-style "V1__init.sql", "V2__add_col.sql", ...,
    /// "V10__rename.sql" sorts ordinally as V1, V10, V2, V3, ..., V9, applying
    /// V10 to the simulator before V2-V9 ran. Zero-padded ("001_", "002_")
    /// and fixed-width date-prefixed ("20230101_") names are unaffected --
    /// digit runs of equal length compare identically either way.
    /// </summary>
    public static int Compare(string a, string b)
    {
        int ia = 0, ib = 0;
        while (ia < a.Length && ib < b.Length)
        {
            if (char.IsDigit(a[ia]) && char.IsDigit(b[ib]))
            {
                int sa = ia, sb = ib;
                while (ia < a.Length && char.IsDigit(a[ia])) ia++;
                while (ib < b.Length && char.IsDigit(b[ib])) ib++;
                string da = a.Substring(sa, ia - sa).TrimStart('0');
                string db = b.Substring(sb, ib - sb).TrimStart('0');
                if (da.Length != db.Length)
                    return da.Length - db.Length;
                int numCmp = string.CompareOrdinal(da, db);
                if (numCmp != 0)
                    return numCmp;
                // Numerically equal (e.g. "007" vs "07"): fall back to raw
                // (padded) run length so the comparison stays deterministic.
                int padCmp = (ia - sa) - (ib - sb);
                if (padCmp != 0)
                    return padCmp;
            }
            else
            {
                if (a[ia] != b[ib])
                    return a[ia] - b[ib];
                ia++;
                ib++;
            }
        }
        return (a.Length - ia) - (b.Length - ib);
    }
}
