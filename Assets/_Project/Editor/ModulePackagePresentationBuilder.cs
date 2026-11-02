using CompanySim;

namespace CompanySimEditor
{
    // Shared entry used by existing scene builders; equipment art is independent of the van.
    static class ModulePackagePresentationBuilder
    {
        public static ModulePackageVisual[] Build(CompanyCatalog catalog) => ModuleEquipmentBuilder.Build(catalog);
    }
}