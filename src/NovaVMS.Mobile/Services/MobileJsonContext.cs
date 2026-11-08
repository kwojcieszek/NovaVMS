using System.Text.Json.Serialization;
using NovaVMS.Core.Models;

namespace NovaVMS.Mobile.Services;

[JsonSerializable(typeof(List<Device>))]
internal partial class MobileJsonContext : JsonSerializerContext;
