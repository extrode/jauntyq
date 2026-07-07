using System.Collections.Generic;
using System.Text;
using JauntyQ.Generated;

namespace JauntyQ.EShopOnWeb.MariaDb.Tests.Repositories;

/// <summary>
/// CatalogItemsSpecification (spec 5) needs a "WHERE id IN (@ids)" query.
/// JauntyQ has no IN-clause list/array parameter binding (confirmed gap -
/// see the test log); EmitParameterBinding only ever emits one
/// scalar DbParameter per name. This bypasses codegen entirely for this one
/// query, using JauntyDb's internal Connection/CurrentTransaction directly
/// so it enlists in the same ambient transaction as generated calls.
/// </summary>
public static class CatalogItemRawQueries
{
    public static List<CatalogItemRow> GetByIds(JauntyDb db, IReadOnlyList<int> ids)
    {
        var result = new List<CatalogItemRow>();
        if (ids.Count == 0)
            return result;

        var sql = new StringBuilder(
            "select id, catalog_type_id, catalog_brand_id, description, name, price, picture_uri from catalog_item where id in (");
        for (int i = 0; i < ids.Count; i++)
        {
            if (i > 0) sql.Append(',');
            sql.Append("@id").Append(i);
        }
        sql.Append(')');

        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = sql.ToString();
        cmd.Transaction = db.CurrentTransaction;
        for (int i = 0; i < ids.Count; i++)
        {
            var p = cmd.CreateParameter();
            p.ParameterName = "@id" + i;
            p.Value = ids[i];
            cmd.Parameters.Add(p);
        }

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            result.Add(CatalogItemRow.Read(reader));
        return result;
    }
}
