using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;

public class Element : INotifyPropertyChanged
{
    private string _name;
    private string _symbol;
    private int _atomicNumber;

    public string OriginalName { get; set; } // New property to store the original name

    public string Name
    {
        get => _name;
        set
        {
            if (_name != value)
            {
                _name = value;
                OnPropertyChanged();
            }
        }
    }

    public string Symbol
    {
        get => _symbol;
        set
        {
            if (_symbol != value)
            {
                _symbol = value;
                OnPropertyChanged();
            }
        }
    }


    public int AtomicNumber
    {
        get => _atomicNumber;
        set
        {
            if (_atomicNumber != value)
            {
                _atomicNumber = value;
                OnPropertyChanged();
            }
        }
    }

    public event PropertyChangedEventHandler PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    [JsonPropertyName("element")]
    public string Title { get; set; }

    [JsonPropertyName("element_atomic_number")]
    public string Number { get; set; }

    [JsonPropertyName("description")]
    public string Description { get; set; }

    [JsonPropertyName("element_year")]
    public string YearDiscovered { get; set; }

    [JsonPropertyName("element_discovered_name")]
    public string DiscoveredBy { get; set; }

    [JsonPropertyName("element_group")]
    public string ElementGroup { get; set; }

    [JsonPropertyName("element_appearance")]
    public string ElementAppearance { get; set; }

    [JsonPropertyName("element_electrons")]
    public string ElementElectrons { get; set; }

    [JsonPropertyName("element_protons")]
    public string ElementProtons { get; set; }

    [JsonPropertyName("element_neutron_common")]
    public string ElementNeutrons { get; set; }

    [JsonPropertyName("wikilink")]
    public string WikipediaLink { get; set; }

    [JsonPropertyName("element_atomicmass")]
    public string ElementAtomicWeight { get; set; }

    [JsonPropertyName("element_density")]
    public string ElementDensity { get; set; }

    [JsonPropertyName("element_electronegativty")]
    public string ElementElectronegativity { get; set; }

    [JsonPropertyName("element_block")]
    public string ElementBlock { get; set; }

    [JsonPropertyName("emission_spectrum")]
    public string EmissionSpectrum { get; set; }

    [JsonPropertyName("short")]
    public string Short { get; set; }

    [JsonPropertyName("link")]
    public string ElementImage { get; set; }

    [JsonPropertyName("element_boiling_kelvin")]
    public string BoilingPointKelvin { get; set; }

    [JsonPropertyName("element_boiling_celsius")]
    public string BoilingPointCelsius { get; set; }

    [JsonPropertyName("element_boiling_fahrenheit")]
    public string BoilingPointFahrenheit { get; set; }

    [JsonPropertyName("element_melting_kelvin")]
    public string MeltingPointKelvin { get; set; }

    [JsonPropertyName("element_melting_celsius")]
    public string MeltingPointCelsius { get; set; }

    [JsonPropertyName("element_melting_fahrenheit")]
    public string MeltingPointFahrenheit { get; set; }

    [JsonPropertyName("oxidation_state_neg")]
    public string OxidationStageNeg { get; set; }

    [JsonPropertyName("oxidation_state_pos")]
    public string OxidationStagePos { get; set; }

    [JsonPropertyName("element_electron_config")]
    public string ElectronConfiguration { get; set; }

    [JsonPropertyName("element_ion_charge")]
    public string IonCharge { get; set; }

    [JsonPropertyName("element_ionization_energy1")]
    public string IonizationEnergies { get; set; }

    [JsonPropertyName("element_atomic_radius_e")]
    public string AtomicRadiusEmpirical { get; set; }

    [JsonPropertyName("element_atomic_radius")]
    public string AtomicRadiusCalculated { get; set; }

    [JsonPropertyName("element_covalent_radius")]
    public string CovalentRadius { get; set; }

    [JsonPropertyName("element_van_der_waals")]
    public string VanDerWallsRadius { get; set; }




    [JsonPropertyName("electrical_type")]
    public string ElectricalType { get; set; }

    [JsonPropertyName("resistivity")]
    public string ElectricalResistivity { get; set; }

    [JsonPropertyName("magnetic_type")]
    public string MagneticType { get; set; }

    [JsonPropertyName("superconducting_point")]
    public string SuperconductingPoint { get; set; }


    [JsonPropertyName("element_fusion_heat")]
    public string FusionHeat { get; set; }

    [JsonPropertyName("element_specific_heat_capacity")]
    public string SpecificHeatCapacity { get; set; }

    [JsonPropertyName("element_vaporization_heat")]
    public string VaporizationHeat { get; set; }

    [JsonPropertyName("element_phase")]
    public string Phase { get; set; }


    [JsonPropertyName("radioactive")]
    public string Radioactive { get; set; }

    [JsonPropertyName("neutron_cross_sectional")]
    public string NeutronCrossSectional { get; set; }


    [JsonPropertyName("mohs_hardness")]
    public string MohsHardness { get; set; }

    [JsonPropertyName("vickers_hardness")]
    public string VickersHardness { get; set; }

    [JsonPropertyName("brinell_hardness")]
    public string BrinellHardness { get; set; }


    [JsonPropertyName("speed_of_sound_solid")]
    public string SoundOfSpeedSolid { get; set; }

    [JsonPropertyName("speed_of_sound_liquid")]
    public string SoundOfSpeedLiquid { get; set; }

    [JsonPropertyName("speed_of_sound_gas")]
    public string SoundOfSpeedGas { get; set; }

    [JsonPropertyName("poisson_ratio")]
    public string PoissonRatio { get; set; }

    [JsonPropertyName("bulk_modulus")]
    public string BulkModulus { get; set; }

    [JsonPropertyName("young_modulus")]
    public string YoungModulus { get; set; }

    [JsonPropertyName("shear_modulus")]
    public string ShearModulus { get; set; }



    [JsonPropertyName("earth_crust")]
    public string EarthCrust { get; set; }

    [JsonPropertyName("earth_soils")]
    public string EarthSoils { get; set; }

    [JsonPropertyName("urban_soils")]
    public string UrbanSoils { get; set; }

    [JsonPropertyName("sea_water")]
    public string SeaWater { get; set; }

    [JsonPropertyName("crustal_rocks")]
    public string CrustalRocks { get; set; }

    [JsonPropertyName("sun")]
    public string Sun { get; set; }

    [JsonPropertyName("solar_system")]
    public string SolarSystem { get; set; }



    [JsonPropertyName("flammability")]
    public int? Flammability { get; set; }

    [JsonPropertyName("health")]
    public int? Health { get; set; }

    [JsonPropertyName("instability")]
    public int? Instability { get; set; }

    [JsonPropertyName("special")]
    public string? Special { get; set; }


    [JsonPropertyName("cas_number")]
    public string CASNumber { get; set; }

    [JsonPropertyName("eg_number")]
    public string EGNumber { get; set; }

    [JsonPropertyName("crystal_structure")]
    public string CrystalStructure { get; set; }


    [JsonPropertyName("lattice_constants")]
    public LatticeConstants LatticeConstants { get; set; }

    [JsonPropertyName("debye_temperature")]
    [JsonConverter(typeof(DebyeTemperatureConverter))]
    public DebyeTemperature DebyeTemperature { get; set; }

}

public class LatticeConstants
{
    [JsonPropertyName("a")]
    public string A { get; set; }

    [JsonPropertyName("b")]
    public string B { get; set; }

    [JsonPropertyName("c")]
    public string C { get; set; }
}

public class DebyeTemperature
{
    [JsonPropertyName("low_temperature_limit")]
    public string LowTemperatureLimit { get; set; }

    [JsonPropertyName("room_temperature")]
    public string RoomTemperature { get; set; }
}


public class DebyeTemperatureConverter : JsonConverter<DebyeTemperature>
{
    public override DebyeTemperature Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            // Handle: "debye_temperature": "1440 K"
            var value = reader.GetString();
            return new DebyeTemperature { LowTemperatureLimit = value };
        }
        else if (reader.TokenType == JsonTokenType.StartObject)
        {
            // Handle: "debye_temperature": { ... }
            using (var doc = JsonDocument.ParseValue(ref reader))
            {
                var obj = doc.RootElement;
                return new DebyeTemperature
                {
                    LowTemperatureLimit = obj.TryGetProperty("low_temperature_limit", out var low) ? low.GetString() : null,
                    RoomTemperature = obj.TryGetProperty("room_temperature", out var room) ? room.GetString() : null
                };
            }
        }
        return null;
    }

    public override void Write(Utf8JsonWriter writer, DebyeTemperature value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        if (value.LowTemperatureLimit != null)
            writer.WriteString("low_temperature_limit", value.LowTemperatureLimit);
        if (value.RoomTemperature != null)
            writer.WriteString("room_temperature", value.RoomTemperature);
        writer.WriteEndObject();
    }
}
