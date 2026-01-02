using Autodesk.Revit.DB;
using SmartTags.ExternalEvents;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SmartTags.Services
{
    public class DirectionTagTypeResolver
    {
        public string DirectionKeyword { get; set; }
        public ElementId LeftTagTypeId { get; set; }
        public ElementId RightTagTypeId { get; set; }
        public ElementId UpTagTypeId { get; set; }
        public ElementId DownTagTypeId { get; set; }
        public ElementId DefaultTagTypeId { get; set; }

        public DirectionTagTypeResolver()
        {
            LeftTagTypeId = ElementId.InvalidElementId;
            RightTagTypeId = ElementId.InvalidElementId;
            UpTagTypeId = ElementId.InvalidElementId;
            DownTagTypeId = ElementId.InvalidElementId;
            DefaultTagTypeId = ElementId.InvalidElementId;
        }

        public ElementId ResolveTagTypeForDirection(PlacementDirection direction)
        {
            ElementId resolvedId = ElementId.InvalidElementId;

            switch (direction)
            {
                case PlacementDirection.Left:
                    resolvedId = LeftTagTypeId;
                    break;
                case PlacementDirection.Right:
                    resolvedId = RightTagTypeId;
                    break;
                case PlacementDirection.Up:
                    resolvedId = UpTagTypeId;
                    break;
                case PlacementDirection.Down:
                    resolvedId = DownTagTypeId;
                    break;
            }

            if (resolvedId == null || resolvedId == ElementId.InvalidElementId)
            {
                return DefaultTagTypeId;
            }

            return resolvedId;
        }

        public static DirectionCheckResult CheckDirectionTagTypes(
            Document doc,
            ElementId tagCategoryId,
            string directionKeyword)
        {
            var result = new DirectionCheckResult();

            if (doc == null || tagCategoryId == null || tagCategoryId == ElementId.InvalidElementId)
            {
                result.ErrorMessage = "Invalid document or tag category.";
                return result;
            }

            if (string.IsNullOrWhiteSpace(directionKeyword))
            {
                result.ErrorMessage = "Direction keyword cannot be empty.";
                return result;
            }

            var tagSymbols = new FilteredElementCollector(doc)
                .OfClass(typeof(FamilySymbol))
                .Cast<FamilySymbol>()
                .Where(symbol => symbol.Category != null && symbol.Category.Id == tagCategoryId)
                .ToList();

            result.LeftMatch = FindBestMatch(tagSymbols, directionKeyword, "Left");
            result.RightMatch = FindBestMatch(tagSymbols, directionKeyword, "Right");
            result.UpMatch = FindBestMatch(tagSymbols, directionKeyword, "Up");
            result.DownMatch = FindBestMatch(tagSymbols, directionKeyword, "Down");

            result.Success = true;
            return result;
        }

        private static TagTypeMatch FindBestMatch(List<FamilySymbol> symbols, string keyword, string direction)
        {
            var match = new TagTypeMatch
            {
                Direction = direction,
                Found = false
            };

            if (symbols == null || symbols.Count == 0)
            {
                return match;
            }

            var candidates = symbols.Where(symbol =>
            {
                var typeName = symbol.Name ?? string.Empty;
                return ContainsDirectionKeyword(typeName, keyword, direction);
            }).ToList();

            if (candidates.Count > 0)
            {
                var bestMatch = candidates.First();
                match.Found = true;
                match.TypeId = bestMatch.Id;
                match.TypeName = $"{bestMatch.Family?.Name} : {bestMatch.Name}";
            }

            return match;
        }

        private static bool ContainsDirectionKeyword(string typeName, string keyword, string direction)
        {
            if (string.IsNullOrWhiteSpace(typeName))
            {
                return false;
            }

            var lowerName = typeName.ToLowerInvariant();
            var lowerDirection = direction.ToLowerInvariant();

            if (lowerName.Contains(lowerDirection))
            {
                return true;
            }

            if (!string.IsNullOrWhiteSpace(keyword))
            {
                var lowerKeyword = keyword.ToLowerInvariant();
                var combinedPattern = $"{lowerKeyword}{lowerDirection}";
                if (lowerName.Contains(combinedPattern))
                {
                    return true;
                }

                var spacedPattern = $"{lowerKeyword} {lowerDirection}";
                if (lowerName.ToLowerInvariant().Replace("-", " ").Replace("_", " ").Contains(spacedPattern))
                {
                    return true;
                }
            }

            return false;
        }
    }

    public class DirectionCheckResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; }
        public TagTypeMatch LeftMatch { get; set; }
        public TagTypeMatch RightMatch { get; set; }
        public TagTypeMatch UpMatch { get; set; }
        public TagTypeMatch DownMatch { get; set; }

        public string GetSummary()
        {
            if (!Success)
            {
                return ErrorMessage ?? "Check failed.";
            }

            var parts = new List<string>();

            AddMatchSummary(parts, LeftMatch);
            AddMatchSummary(parts, RightMatch);
            AddMatchSummary(parts, UpMatch);
            AddMatchSummary(parts, DownMatch);

            if (parts.Count == 0)
            {
                return "No matching tag types found for any direction.";
            }

            return string.Join("\n", parts);
        }

        private void AddMatchSummary(List<string> parts, TagTypeMatch match)
        {
            if (match == null)
            {
                return;
            }

            if (match.Found)
            {
                parts.Add($"{match.Direction}: {match.TypeName}");
            }
            else
            {
                parts.Add($"{match.Direction}: No matching tag type found");
            }
        }
    }

    public class TagTypeMatch
    {
        public string Direction { get; set; }
        public bool Found { get; set; }
        public ElementId TypeId { get; set; }
        public string TypeName { get; set; }
    }
}
