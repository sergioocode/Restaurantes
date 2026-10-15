using System.Text.Json;
using Restaurantes.Catalog.Api.Controllers;
using Xunit;

namespace Restaurantes.Catalog.Infrastructure.Tests;

public sealed class CatalogDesignInputTests
{
    [Theory]
    [InlineData(
        typeof(CatalogDesignController.TaxInput),
        """{"Code":"IVA","Name":"IVA","Rate":10,"IsActive":true}""",
        """{"Code":"IVA","Name":"IVA","IsActive":true}"""
    )]
    [InlineData(
        typeof(CatalogDesignController.TaxInput),
        """{"Code":"IVA","Name":"IVA","Rate":10,"IsActive":true}""",
        """{"Code":"IVA","Name":"IVA","Rate":10}"""
    )]
    [InlineData(
        typeof(CatalogDesignController.AllergenInput),
        """{"Code":"GLUTEN","Name":"Gluten","Description":"","IsActive":true}""",
        """{"Code":"GLUTEN","Name":"Gluten","Description":""}"""
    )]
    [InlineData(
        typeof(CatalogDesignController.DefinitionInput),
        """{"IsCombo":true,"AllowsExtras":false,"Groups":[]}""",
        """{"AllowsExtras":false,"Groups":[]}"""
    )]
    [InlineData(
        typeof(CatalogDesignController.DefinitionInput),
        """{"IsCombo":true,"AllowsExtras":false,"Groups":[]}""",
        """{"IsCombo":true,"Groups":[]}"""
    )]
    [InlineData(
        typeof(CatalogDesignController.GroupInput),
        """{"Name":"Salsas","Kind":"Extra","MinSelections":0,"MaxSelections":1,"ProductIds":[]}""",
        """{"Name":"Salsas","Kind":"Extra","MaxSelections":1,"ProductIds":[]}"""
    )]
    [InlineData(
        typeof(CatalogDesignController.GroupInput),
        """{"Name":"Salsas","Kind":"Extra","MinSelections":0,"MaxSelections":1,"ProductIds":[]}""",
        """{"Name":"Salsas","Kind":"Extra","MinSelections":0,"ProductIds":[]}"""
    )]
    [InlineData(
        typeof(CatalogDesignController.PriceInput),
        """{"Channel":"LOCAL","Format":"PLATO","Amount":12.5}""",
        """{"Channel":"LOCAL","Format":"PLATO"}"""
    )]
    public void Design_inputs_require_explicit_numeric_and_boolean_values(
        Type inputType,
        string validJson,
        string missingRequiredJson
    )
    {
        Assert.NotNull(JsonSerializer.Deserialize(validJson, inputType));
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize(missingRequiredJson, inputType)
        );
    }
}
