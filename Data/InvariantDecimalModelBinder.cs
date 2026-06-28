using System.Globalization;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace GeneradorTurnos.Data;

/// <summary>
/// Con la cultura es-CO (miles con punto), los &lt;input type="number"&gt; envían el valor con
/// punto decimal (formato invariante). Este binder parsea decimales de forma invariante para
/// que precios como 25000 o 25000.50 se interpreten bien sin importar la cultura de la petición.
/// </summary>
public class InvariantDecimalModelBinder : IModelBinder
{
    public Task BindModelAsync(ModelBindingContext ctx)
    {
        var result = ctx.ValueProvider.GetValue(ctx.ModelName);
        if (result == ValueProviderResult.None) return Task.CompletedTask;

        ctx.ModelState.SetModelValue(ctx.ModelName, result);
        var raw = result.FirstValue;

        if (string.IsNullOrWhiteSpace(raw))
        {
            if (Nullable.GetUnderlyingType(ctx.ModelType) != null)
                ctx.Result = ModelBindingResult.Success(null);
            return Task.CompletedTask;
        }

        raw = raw.Trim();
        if (decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var dInv))
            ctx.Result = ModelBindingResult.Success(dInv);
        else if (decimal.TryParse(raw, NumberStyles.Number, CultureInfo.GetCultureInfo("es-CO"), out var dCo))
            ctx.Result = ModelBindingResult.Success(dCo);
        else
            ctx.ModelState.TryAddModelError(ctx.ModelName, "Número inválido.");

        return Task.CompletedTask;
    }
}

public class InvariantDecimalModelBinderProvider : IModelBinderProvider
{
    public IModelBinder? GetBinder(ModelBinderProviderContext context)
    {
        var t = context.Metadata.ModelType;
        return t == typeof(decimal) || t == typeof(decimal?)
            ? new InvariantDecimalModelBinder() : null;
    }
}
