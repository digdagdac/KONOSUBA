using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Overbless.Editor.Evidence
{
    public sealed class EvidenceValidationResult
    {
        private EvidenceValidationResult(bool isValid, string code, string message)
        {
            IsValid = isValid;
            Code = code;
            Message = message;
        }

        public bool IsValid { get; }
        public string Code { get; }
        public string Message { get; }

        public static EvidenceValidationResult Pass() => new EvidenceValidationResult(true, "OK", "OK");
        public static EvidenceValidationResult Fail(string code, string message) => new EvidenceValidationResult(false, code ?? "INVALID", message ?? "Evidence validation failed.");
    }

    public sealed class PerformanceBucket
    {
        public PerformanceBucket(int index, long startUs, long endUs, long completedFrames, double minFpsEquivalent)
        {
            Index = index;
            StartUs = startUs;
            EndUs = endUs;
            CompletedFrames = completedFrames;
            MinFpsEquivalent = minFpsEquivalent;
        }

        public int Index { get; }
        public long StartUs { get; }
        public long EndUs { get; }
        public long CompletedFrames { get; }
        public double MinFpsEquivalent { get; }
    }

    /// <summary>Deterministic semantic checks that JSON Schema alone cannot express.</summary>
    public static class EvidenceSchemaValidator
    {
        private static readonly string[] AudioEvents = { "ArcherReady", "DasherReady", "ExitOpened" };
        private const long BucketDurationMicroseconds = 1000000L;
        private const int BucketCount = 60;

        public static EvidenceValidationResult ValidateRequiredOnlyObject(CanonicalJsonValue value, IEnumerable<string> requiredKeys)
        {
            if (value == null || value.Kind != CanonicalJsonKind.Object) return EvidenceValidationResult.Fail("TYPE", "Expected an object.");
            if (requiredKeys == null) throw new ArgumentNullException(nameof(requiredKeys));

            var expected = new HashSet<string>(StringComparer.Ordinal);
            foreach (var key in requiredKeys)
            {
                if (string.IsNullOrEmpty(key)) throw new ArgumentException("Required keys must be nonempty.", nameof(requiredKeys));
                if (!expected.Add(key)) throw new ArgumentException("Required keys must be unique.", nameof(requiredKeys));
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            var missing = false;
            var additional = false;
            var duplicate = false;
            var unsorted = false;
            string previous = null;
            foreach (var property in value.Properties)
            {
                if (previous != null && string.CompareOrdinal(previous, property.Name) > 0) unsorted = true;
                previous = property.Name;
                if (!seen.Add(property.Name)) duplicate = true;
                if (!expected.Contains(property.Name)) additional = true;
            }

            foreach (var key in expected)
            {
                if (!seen.Contains(key))
                {
                    missing = true;
                    break;
                }
            }

            if (missing) return EvidenceValidationResult.Fail("MISSING_KEY", "A required key is missing.");
            if (additional) return EvidenceValidationResult.Fail("ADDITIONAL_KEY", "An additional key is present.");
            if (unsorted) return EvidenceValidationResult.Fail("UNSORTED", "Object keys are not ordinal sorted.");
            if (duplicate) return EvidenceValidationResult.Fail("DUPLICATE", "An object key is duplicated.");
            return EvidenceValidationResult.Pass();
        }

        public static EvidenceValidationResult ValidateSchemaObject(CanonicalJsonValue value, string schemaLiteral, IEnumerable<string> requiredKeys)
        {
            if (value == null || value.Kind != CanonicalJsonKind.Object) return EvidenceValidationResult.Fail("TYPE", "Expected an object.");
            if (string.IsNullOrEmpty(schemaLiteral)) throw new ArgumentException("Schema literal is required.", nameof(schemaLiteral));

            var shape = ValidateRequiredOnlyObject(value, requiredKeys);
            if (!shape.IsValid) return shape;

            CanonicalJsonValue schema;
            if (!value.TryGetSingleProperty("schema", out schema) || schema.Kind != CanonicalJsonKind.String)
            {
                return EvidenceValidationResult.Fail("TYPE", "Schema must be a string.");
            }
            if (!string.Equals(schema.StringValue, schemaLiteral, StringComparison.Ordinal))
            {
                return EvidenceValidationResult.Fail("UNKNOWN_SCHEMA", "Schema literal is not expected.");
            }

            EvidenceSchemaDefinition definition;
            if (!EvidenceSchemaWriter.TryGetSchemaDefinition(schemaLiteral, out definition))
            {
                return EvidenceValidationResult.Fail("UNKNOWN_SCHEMA", "Schema literal is not registered.");
            }
            return ValidateAgainstSchema(value, definition.Document, 0);
        }

        private static EvidenceValidationResult ValidateAgainstSchema(CanonicalJsonValue value, CanonicalJsonValue schema, int depth)
        {
            if (value == null || schema == null || schema.Kind != CanonicalJsonKind.Object) return EvidenceValidationResult.Fail("TYPE", "Schema validation requires values and schemas to be objects.");
            if (depth > CanonicalJson.MaximumNestingDepth) return EvidenceValidationResult.Fail("TYPE", "Schema validation exceeded the maximum nesting depth.");

            CanonicalJsonValue alternatives;
            if (schema.TryGetSingleProperty("oneOf", out alternatives))
            {
                if (alternatives.Kind != CanonicalJsonKind.Array || alternatives.Items.Count == 0) return EvidenceValidationResult.Fail("TYPE", "Schema oneOf is invalid.");
                EvidenceValidationResult firstFailure = null;
                var matches = 0;
                foreach (var alternative in alternatives.Items)
                {
                    var result = ValidateAgainstSchema(value, alternative, depth + 1);
                    if (result.IsValid) matches++;
                    else if (firstFailure == null) firstFailure = result;
                }
                return matches == 1
                    ? EvidenceValidationResult.Pass()
                    : matches == 0
                        ? firstFailure ?? EvidenceValidationResult.Fail("TYPE", "No oneOf schema matched.")
                        : EvidenceValidationResult.Fail("TYPE", "More than one oneOf schema matched.");
            }

            if (schema.TryGetSingleProperty("anyOf", out alternatives))
            {
                if (alternatives.Kind != CanonicalJsonKind.Array || alternatives.Items.Count == 0) return EvidenceValidationResult.Fail("TYPE", "Schema anyOf is invalid.");
                EvidenceValidationResult firstFailure = null;
                foreach (var alternative in alternatives.Items)
                {
                    var result = ValidateAgainstSchema(value, alternative, depth + 1);
                    if (result.IsValid) return result;
                    if (firstFailure == null) firstFailure = result;
                }
                return firstFailure ?? EvidenceValidationResult.Fail("TYPE", "No anyOf schema matched.");
            }

            CanonicalJsonValue expectedType;
            if (schema.TryGetSingleProperty("type", out expectedType))
            {
                if (expectedType.Kind != CanonicalJsonKind.String || !MatchesType(value, expectedType.StringValue))
                {
                    return EvidenceValidationResult.Fail("TYPE", "Value does not match the schema type.");
                }
            }

            CanonicalJsonValue constant;
            if (schema.TryGetSingleProperty("const", out constant) && !ValuesEqual(value, constant))
            {
                return EvidenceValidationResult.Fail("ENUM", "Value does not match the schema constant.");
            }

            CanonicalJsonValue enumeration;
            if (schema.TryGetSingleProperty("enum", out enumeration))
            {
                if (enumeration.Kind != CanonicalJsonKind.Array) return EvidenceValidationResult.Fail("TYPE", "Schema enum is invalid.");
                var found = false;
                foreach (var item in enumeration.Items)
                {
                    if (ValuesEqual(value, item))
                    {
                        found = true;
                        break;
                    }
                }
                if (!found) return EvidenceValidationResult.Fail("ENUM", "Value is not in the schema enum.");
            }

            if (value.Kind == CanonicalJsonKind.String)
            {
                long minimumStringLength = -1;
                CanonicalJsonValue minimumLength;
                if (schema.TryGetSingleProperty("minLength", out minimumLength))
                {
                    if (!TryGetInteger(minimumLength, out minimumStringLength) ||
                        minimumStringLength < 0 ||
                        value.StringValue.Length < minimumStringLength)
                    {
                        return EvidenceValidationResult.Fail("TYPE", "String is shorter than the schema minimum length.");
                    }
                }

                CanonicalJsonValue maximumLength;
                if (schema.TryGetSingleProperty("maxLength", out maximumLength))
                {
                    long maximumStringLength;
                    if (!TryGetInteger(maximumLength, out maximumStringLength) ||
                        maximumStringLength < 0 ||
                        (minimumStringLength >= 0 && maximumStringLength < minimumStringLength) ||
                        value.StringValue.Length > maximumStringLength)
                    {
                        return EvidenceValidationResult.Fail("TYPE", "String exceeds the schema maximum length.");
                    }
                }

                CanonicalJsonValue pattern;
                if (schema.TryGetSingleProperty("pattern", out pattern))
                {
                    if (pattern.Kind != CanonicalJsonKind.String) return EvidenceValidationResult.Fail("TYPE", "Schema pattern is invalid.");
                    try
                    {
                        if (!Regex.IsMatch(value.StringValue, pattern.StringValue, RegexOptions.CultureInvariant))
                        {
                            return EvidenceValidationResult.Fail("TYPE", "String does not match the schema pattern.");
                        }
                    }
                    catch (ArgumentException)
                    {
                        return EvidenceValidationResult.Fail("TYPE", "Schema pattern is invalid.");
                    }
                }
            }

            if (value.Kind == CanonicalJsonKind.Number)
            {
                if (double.IsNaN(value.NumberValue) || double.IsInfinity(value.NumberValue))
                {
                    return EvidenceValidationResult.Fail("TYPE", "Numbers must be finite.");
                }

                CanonicalJsonValue minimum;
                if (schema.TryGetSingleProperty("minimum", out minimum) && (minimum.Kind != CanonicalJsonKind.Number || value.NumberValue < minimum.NumberValue))
                {
                    return EvidenceValidationResult.Fail("TYPE", "Number is below the schema minimum.");
                }

                CanonicalJsonValue maximum;
                if (schema.TryGetSingleProperty("maximum", out maximum) && (maximum.Kind != CanonicalJsonKind.Number || value.NumberValue > maximum.NumberValue))
                {
                    return EvidenceValidationResult.Fail("TYPE", "Number exceeds the schema maximum.");
                }
            }

            if (value.Kind == CanonicalJsonKind.Array)
            {
                CanonicalJsonValue minimumItems;
                if (schema.TryGetSingleProperty("minItems", out minimumItems))
                {
                    long minimum;
                    if (!TryGetInteger(minimumItems, out minimum) || minimum < 0 || value.Items.Count < minimum)
                    {
                        return EvidenceValidationResult.Fail("TYPE", "Array is shorter than the schema minimum.");
                    }
                }

                CanonicalJsonValue maximumItems;
                if (schema.TryGetSingleProperty("maxItems", out maximumItems))
                {
                    long maximum;
                    if (!TryGetInteger(maximumItems, out maximum) || maximum < 0 || value.Items.Count > maximum)
                    {
                        return EvidenceValidationResult.Fail("TYPE", "Array exceeds the schema maximum.");
                    }
                }

                CanonicalJsonValue uniqueItems;
                if (schema.TryGetSingleProperty("uniqueItems", out uniqueItems) && uniqueItems.Kind == CanonicalJsonKind.Boolean && uniqueItems.BooleanValue)
                {
                    var unique = new HashSet<string>(StringComparer.Ordinal);
                    foreach (var item in value.Items)
                    {
                        if (!unique.Add(CanonicalJson.Serialize(item))) return EvidenceValidationResult.Fail("DUPLICATE", "Array contains duplicate items.");
                    }
                }

                CanonicalJsonValue itemSchema;
                if (schema.TryGetSingleProperty("items", out itemSchema))
                {
                    foreach (var item in value.Items)
                    {
                        var result = ValidateAgainstSchema(item, itemSchema, depth + 1);
                        if (!result.IsValid) return result;
                    }
                }
            }

            if (value.Kind == CanonicalJsonKind.Object)
            {
                var order = ValidateObjectMemberOrder(value);
                if (!order.IsValid) return order;

                CanonicalJsonValue required;
                if (schema.TryGetSingleProperty("required", out required))
                {
                    if (required.Kind != CanonicalJsonKind.Array) return EvidenceValidationResult.Fail("TYPE", "Schema required list is invalid.");
                    foreach (var name in required.Items)
                    {
                        CanonicalJsonValue ignored;
                        if (name.Kind != CanonicalJsonKind.String || !value.TryGetSingleProperty(name.StringValue, out ignored))
                        {
                            return EvidenceValidationResult.Fail("MISSING_KEY", "A required key is missing.");
                        }
                    }
                }

                CanonicalJsonValue properties;
                if (schema.TryGetSingleProperty("properties", out properties) && properties.Kind != CanonicalJsonKind.Object)
                {
                    return EvidenceValidationResult.Fail("TYPE", "Schema properties are invalid.");
                }

                CanonicalJsonValue additionalProperties;
                var hasAdditionalProperties = schema.TryGetSingleProperty("additionalProperties", out additionalProperties);
                foreach (var property in value.Properties)
                {
                    CanonicalJsonValue propertySchema;
                    if (properties != null && properties.TryGetSingleProperty(property.Name, out propertySchema))
                    {
                        var result = ValidateAgainstSchema(property.Value, propertySchema, depth + 1);
                        if (!result.IsValid) return result;
                        continue;
                    }

                    if (!hasAdditionalProperties) continue;
                    if (additionalProperties.Kind == CanonicalJsonKind.Boolean)
                    {
                        if (!additionalProperties.BooleanValue) return EvidenceValidationResult.Fail("ADDITIONAL_KEY", "An additional key is present.");
                        continue;
                    }

                    var additionalResult = ValidateAgainstSchema(property.Value, additionalProperties, depth + 1);
                    if (!additionalResult.IsValid) return additionalResult;
                }
            }

            return EvidenceValidationResult.Pass();
        }

        private static EvidenceValidationResult ValidateObjectMemberOrder(CanonicalJsonValue value)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            string previous = null;
            foreach (var property in value.Properties)
            {
                if (!seen.Add(property.Name)) return EvidenceValidationResult.Fail("DUPLICATE", "An object key is duplicated.");
                if (previous != null && CanonicalJson.CompareUtf8Ordinal(previous, property.Name) > 0)
                {
                    return EvidenceValidationResult.Fail("UNSORTED", "Object keys are not UTF-8 ordinal sorted.");
                }
                previous = property.Name;
            }
            return EvidenceValidationResult.Pass();
        }

        private static bool MatchesType(CanonicalJsonValue value, string type)
        {
            switch (type)
            {
                case "object": return value.Kind == CanonicalJsonKind.Object;
                case "array": return value.Kind == CanonicalJsonKind.Array;
                case "string": return value.Kind == CanonicalJsonKind.String;
                case "boolean": return value.Kind == CanonicalJsonKind.Boolean;
                case "number": return value.Kind == CanonicalJsonKind.Number && !double.IsNaN(value.NumberValue) && !double.IsInfinity(value.NumberValue);
                case "integer":
                    long ignored;
                    return TryGetInteger(value, out ignored);
                case "null": return value.Kind == CanonicalJsonKind.Null;
                default: return false;
            }
        }

        private static bool ValuesEqual(CanonicalJsonValue left, CanonicalJsonValue right)
        {
            if (left == null || right == null || left.Kind != right.Kind) return false;
            switch (left.Kind)
            {
                case CanonicalJsonKind.Null: return true;
                case CanonicalJsonKind.Boolean: return left.BooleanValue == right.BooleanValue;
                case CanonicalJsonKind.Number: return left.NumberValue == right.NumberValue;
                case CanonicalJsonKind.String: return string.Equals(left.StringValue, right.StringValue, StringComparison.Ordinal);
                default: return string.Equals(CanonicalJson.Serialize(left), CanonicalJson.Serialize(right), StringComparison.Ordinal);
            }
        }

        public static EvidenceValidationResult ValidateCriteria(IReadOnlyList<CanonicalJsonValue> criterionIds, bool requireAllCriteria)
        {
            if (criterionIds == null || criterionIds.Count == 0) return EvidenceValidationResult.Fail("TYPE", "Criterion IDs must be a nonempty array.");
            var seen = new HashSet<string>(StringComparer.Ordinal);
            string previous = null;
            foreach (var value in criterionIds)
            {
                if (value == null || value.Kind != CanonicalJsonKind.String || string.IsNullOrEmpty(value.StringValue)) return EvidenceValidationResult.Fail("TYPE", "Criterion IDs must be nonempty strings.");
                if (previous != null && CanonicalJson.CompareUtf8Ordinal(previous, value.StringValue) > 0) return EvidenceValidationResult.Fail("UNSORTED", "Criterion IDs are not UTF-8 ordinal sorted.");
                if (!seen.Add(value.StringValue)) return EvidenceValidationResult.Fail("DUPLICATE", "Criterion ID is duplicated.");
                if (!Contains(EvidenceContracts.CriterionIds, value.StringValue)) return EvidenceValidationResult.Fail("UNKNOWN_CRITERION", "Criterion ID is unknown.");
                previous = value.StringValue;
            }

            if (requireAllCriteria && (seen.Count != EvidenceContracts.CriterionIds.Length || !ContainsAll(seen, EvidenceContracts.CriterionIds)))
            {
                return EvidenceValidationResult.Fail("UNCOVERED", "The complete criterion set is required.");
            }

            return EvidenceValidationResult.Pass();
        }

        public static string SelectDetail(string checkId, IEnumerable<string> failureCodes)
        {
            if (failureCodes == null) throw new ArgumentNullException(nameof(failureCodes));
            var failures = new HashSet<string>(failureCodes, StringComparer.Ordinal);
            return EvidenceContracts.SelectDetail(checkId, failures);
        }

        public static EvidenceValidationResult ValidateReportChecks(CanonicalJsonValue checks)
        {
            if (checks == null || checks.Kind != CanonicalJsonKind.Array) return EvidenceValidationResult.Fail("TYPE", "Checks must be an array.");
            if (checks.Items.Count != EvidenceContracts.Checks.Length) return EvidenceValidationResult.Fail("MISSING_KEY", "Every fixed check is required exactly once.");

            for (var index = 0; index < EvidenceContracts.Checks.Length; index++)
            {
                var check = checks.Items[index];
                var shape = ValidateRequiredOnlyObject(check, new[] { "checkId", "status", "detailCode" });
                if (!shape.IsValid) return shape;
                CanonicalJsonValue checkId;
                CanonicalJsonValue status;
                CanonicalJsonValue detailCode;
                check.TryGetSingleProperty("checkId", out checkId);
                check.TryGetSingleProperty("status", out status);
                check.TryGetSingleProperty("detailCode", out detailCode);
                if (checkId.Kind != CanonicalJsonKind.String || status.Kind != CanonicalJsonKind.String || detailCode.Kind != CanonicalJsonKind.String) return EvidenceValidationResult.Fail("TYPE", "Check fields must be strings.");
                if (!string.Equals(checkId.StringValue, EvidenceContracts.Checks[index], StringComparison.Ordinal)) return EvidenceValidationResult.Fail("UNSORTED", "Checks are not in the fixed order.");
                if (status.StringValue != "PASS" && status.StringValue != "FAIL") return EvidenceValidationResult.Fail("ENUM", "Check status is invalid.");
                if (status.StringValue == "PASS" && detailCode.StringValue != "OK") return EvidenceValidationResult.Fail("ENUM", "PASS requires detailCode OK.");
                if (status.StringValue == "FAIL" && !Contains(EvidenceContracts.DetailPrecedence[checkId.StringValue], detailCode.StringValue)) return EvidenceValidationResult.Fail("ENUM", "FAIL detailCode is invalid for this check.");
            }

            return EvidenceValidationResult.Pass();
        }

        public static EvidenceValidationResult ValidateAudioPermutation(IEnumerable<string> eventOrder)
        {
            if (eventOrder == null) return EvidenceValidationResult.Fail("EVENT_MISSING", "Audio event order is required.");
            var values = new List<string>(eventOrder);
            if (values.Count != AudioEvents.Length) return EvidenceValidationResult.Fail("EVENT_MISSING", "Audio event order must contain three events.");
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var value in values)
            {
                if (!Contains(AudioEvents, value)) return EvidenceValidationResult.Fail("ENUM", "Audio event is unknown.");
                if (!seen.Add(value)) return EvidenceValidationResult.Fail("EVENT_DUPLICATE", "Audio event is duplicated.");
            }
            return seen.Count == AudioEvents.Length ? EvidenceValidationResult.Pass() : EvidenceValidationResult.Fail("EVENT_MISSING", "Audio event is missing.");
        }

        public static EvidenceValidationResult ValidateAudioRandomization(CanonicalJsonValue value, string candidateId, string buildManifestSha256, IEnumerable<string> testerIds)
        {
            var shape = ValidateSchemaObject(value, EvidenceContracts.AudioRandomization, new[] { "schema", "candidateId", "buildManifestSha256", "seed", "orders" });
            if (!shape.IsValid) return shape;
            if (string.IsNullOrEmpty(candidateId) || !CanonicalJson.IsLowerSha256(buildManifestSha256)) return EvidenceValidationResult.Fail("TYPE", "Expected candidate and build binding are required.");

            CanonicalJsonValue actualCandidate;
            CanonicalJsonValue actualBuild;
            CanonicalJsonValue seed;
            CanonicalJsonValue orders;
            value.TryGetSingleProperty("candidateId", out actualCandidate);
            value.TryGetSingleProperty("buildManifestSha256", out actualBuild);
            value.TryGetSingleProperty("seed", out seed);
            value.TryGetSingleProperty("orders", out orders);
            if (actualCandidate.Kind != CanonicalJsonKind.String || actualBuild.Kind != CanonicalJsonKind.String || seed.Kind != CanonicalJsonKind.Number || orders.Kind != CanonicalJsonKind.Array)
            {
                return EvidenceValidationResult.Fail("TYPE", "Audio randomization field types are invalid.");
            }
            if (string.IsNullOrEmpty(actualCandidate.StringValue) || !string.Equals(actualCandidate.StringValue, candidateId, StringComparison.Ordinal)) return EvidenceValidationResult.Fail("ID_MISMATCH", "Candidate binding does not match.");
            if (!string.Equals(actualBuild.StringValue, buildManifestSha256, StringComparison.Ordinal) || !CanonicalJson.IsLowerSha256(actualBuild.StringValue)) return EvidenceValidationResult.Fail("BUILD_HASH", "Build manifest hash does not match.");
            long seedValue;
            if (!TryGetInteger(seed, out seedValue) || seedValue < 0 || seedValue > int.MaxValue) return EvidenceValidationResult.Fail("TYPE", "Seed must be an integer in range.");
            if (orders.Items.Count != 3) return EvidenceValidationResult.Fail("COUNT", "Exactly three tester orders are required.");

            var expectedTesters = new List<string>();
            if (testerIds == null) return EvidenceValidationResult.Fail("COUNT", "Expected tester IDs are required.");
            foreach (var testerId in testerIds)
            {
                if (string.IsNullOrEmpty(testerId)) return EvidenceValidationResult.Fail("TYPE", "Tester IDs must be nonempty strings.");
                expectedTesters.Add(testerId);
            }
            expectedTesters.Sort(CanonicalJson.CompareUtf8Ordinal);
            if (expectedTesters.Count != 3 || HasDuplicates(expectedTesters)) return EvidenceValidationResult.Fail("COUNT", "Exactly three unique expected tester IDs are required.");

            var actualTesters = new List<string>();
            string previousTester = null;
            foreach (var order in orders.Items)
            {
                var orderShape = ValidateRequiredOnlyObject(order, new[] { "testerId", "eventOrder" });
                if (!orderShape.IsValid) return orderShape;
                CanonicalJsonValue testerId;
                CanonicalJsonValue eventOrder;
                order.TryGetSingleProperty("testerId", out testerId);
                order.TryGetSingleProperty("eventOrder", out eventOrder);
                if (testerId.Kind != CanonicalJsonKind.String || eventOrder.Kind != CanonicalJsonKind.Array || string.IsNullOrEmpty(testerId.StringValue)) return EvidenceValidationResult.Fail("TYPE", "Audio order field types are invalid.");
                if (previousTester != null && CanonicalJson.CompareUtf8Ordinal(previousTester, testerId.StringValue) > 0) return EvidenceValidationResult.Fail("UNSORTED", "Audio orders are not sorted by tester ID.");
                previousTester = testerId.StringValue;
                actualTesters.Add(testerId.StringValue);

                var events = new List<string>();
                foreach (var eventValue in eventOrder.Items)
                {
                    if (eventValue.Kind != CanonicalJsonKind.String) return EvidenceValidationResult.Fail("TYPE", "Audio events must be strings.");
                    events.Add(eventValue.StringValue);
                }
                var permutation = ValidateAudioPermutation(events);
                if (!permutation.IsValid) return permutation;
            }

            if (HasDuplicates(actualTesters)) return EvidenceValidationResult.Fail("DUPLICATE", "Tester ID is duplicated.");
            actualTesters.Sort(CanonicalJson.CompareUtf8Ordinal);
            if (!ListsEqual(actualTesters, expectedTesters)) return EvidenceValidationResult.Fail("ID_MISMATCH", "Audio randomization testers do not match the three blind testers.");
            return EvidenceValidationResult.Pass();
        }

        public static EvidenceValidationResult ValidatePerformanceBuckets(
            long bucketOriginMicroseconds,
            IReadOnlyList<PerformanceBucket> buckets,
            IEnumerable<long> completionTimesUs)
        {
            if (completionTimesUs == null)
            {
                return EvidenceValidationResult.Fail("FRAME_COUNT", "Completion timestamps are required.");
            }

            if (bucketOriginMicroseconds < 0)
            {
                return EvidenceValidationResult.Fail("TYPE", "Bucket origin must be nonnegative.");
            }

            if (buckets == null || buckets.Count != BucketCount)
            {
                return EvidenceValidationResult.Fail("BUCKET_COUNT", "Exactly sixty buckets are required.");
            }

            var times = new List<long>(completionTimesUs);
            long declaredFrameCount = 0;
            for (var index = 0; index < BucketCount; index++)
            {
                var bucket = buckets[index];
                if (bucket == null || bucket.Index != index)
                {
                    return EvidenceValidationResult.Fail("BUCKET_COUNT", "Buckets must be sorted and indexed zero through fifty-nine.");
                }

                long start;
                long end;
                try
                {
                    start = checked(bucketOriginMicroseconds + index * BucketDurationMicroseconds);
                    end = checked(start + BucketDurationMicroseconds);
                }
                catch (OverflowException)
                {
                    return EvidenceValidationResult.Fail("TYPE", "Bucket origin overflows the fixed range.");
                }

                if (bucket.StartUs != start ||
                    bucket.EndUs != end ||
                    bucket.EndUs <= bucket.StartUs ||
                    bucket.CompletedFrames < 0 ||
                    double.IsNaN(bucket.MinFpsEquivalent) ||
                    double.IsInfinity(bucket.MinFpsEquivalent) ||
                    bucket.MinFpsEquivalent < 0d)
                {
                    return EvidenceValidationResult.Fail("FRAME_COUNT", "Bucket boundaries or values do not match the fixed formula.");
                }

                if (bucket.MinFpsEquivalent != bucket.CompletedFrames)
                {
                    return EvidenceValidationResult.Fail("FRAME_COUNT", "minFpsEquivalent must equal completedFrames for one-second buckets.");
                }

                if (bucket.CompletedFrames > long.MaxValue - declaredFrameCount)
                {
                    return EvidenceValidationResult.Fail("FRAME_COUNT", "Frame count overflows.");
                }

                declaredFrameCount += bucket.CompletedFrames;
            }

            long sampleEnd;
            try
            {
                sampleEnd = checked(bucketOriginMicroseconds + BucketCount * BucketDurationMicroseconds);
            }
            catch (OverflowException)
            {
                return EvidenceValidationResult.Fail("TYPE", "Bucket origin overflows the fixed range.");
            }

            var actualCounts = new long[BucketCount];
            foreach (var completionTime in times)
            {
                if (completionTime < bucketOriginMicroseconds || completionTime >= sampleEnd)
                {
                    return EvidenceValidationResult.Fail("FRAME_COUNT", "A completion timestamp is outside the half-open sixty-second sample.");
                }

                var bucketIndex = (int)((completionTime - bucketOriginMicroseconds) / BucketDurationMicroseconds);
                actualCounts[bucketIndex]++;
            }

            if (times.Count != declaredFrameCount)
            {
                return EvidenceValidationResult.Fail("FRAME_COUNT", "CSV completion count does not equal the bucket sum.");
            }

            for (var index = 0; index < BucketCount; index++)
            {
                if (actualCounts[index] != buckets[index].CompletedFrames)
                {
                    return EvidenceValidationResult.Fail("FRAME_COUNT", "Bucket completedFrames does not equal its half-open CSV interval.");
                }
            }

            return EvidenceValidationResult.Pass();
        }

        public static EvidenceValidationResult ValidatePerformance(CanonicalJsonValue value, IEnumerable<long> completionTimesUs, IEnumerable<long> frameDurationsUs, string expectedBrowser, string expectedResolution, string expectedScenario)
        {
            if (completionTimesUs == null || frameDurationsUs == null)
            {
                return EvidenceValidationResult.Fail("FRAME_COUNT", "Performance completion and duration records are required.");
            }
            if (string.IsNullOrEmpty(expectedBrowser) || string.IsNullOrEmpty(expectedResolution) || string.IsNullOrEmpty(expectedScenario))
            {
                return EvidenceValidationResult.Fail("MISSING_CELL", "Performance matrix identifiers are required.");
            }
            if (value != null && value.Kind == CanonicalJsonKind.Object)
            {
                CanonicalJsonValue foregroundValue;
                CanonicalJsonValue pauseValue;
                if (value.TryGetSingleProperty("allForeground", out foregroundValue) &&
                    value.TryGetSingleProperty("noPause", out pauseValue) &&
                    foregroundValue.Kind == CanonicalJsonKind.Boolean &&
                    pauseValue.Kind == CanonicalJsonKind.Boolean &&
                    (!foregroundValue.BooleanValue || !pauseValue.BooleanValue))
                {
                    return EvidenceValidationResult.Fail(
                        "FOREGROUND",
                        "Performance sample requires foreground and no-pause evidence.");
                }
            }
            var shape = ValidateSchemaObject(value, "overbless.performance/v1", new[] { "schema", "browser", "resolution", "scenario", "warmupSeconds", "sampleSeconds", "bucketOriginMicroseconds", "buckets", "allForeground", "noPause", "status", "longestFrameUs", "p95FrameUs" });
            if (!shape.IsValid) return shape;

            CanonicalJsonValue browser;
            CanonicalJsonValue resolution;
            CanonicalJsonValue scenario;
            CanonicalJsonValue warmup;
            CanonicalJsonValue sample;
            CanonicalJsonValue origin;
            CanonicalJsonValue buckets;
            CanonicalJsonValue foreground;
            CanonicalJsonValue pause;
            CanonicalJsonValue status;
            CanonicalJsonValue longest;
            CanonicalJsonValue p95;
            value.TryGetSingleProperty("browser", out browser);
            value.TryGetSingleProperty("resolution", out resolution);
            value.TryGetSingleProperty("scenario", out scenario);
            value.TryGetSingleProperty("warmupSeconds", out warmup);
            value.TryGetSingleProperty("sampleSeconds", out sample);
            value.TryGetSingleProperty("bucketOriginMicroseconds", out origin);
            value.TryGetSingleProperty("buckets", out buckets);
            value.TryGetSingleProperty("allForeground", out foreground);
            value.TryGetSingleProperty("noPause", out pause);
            value.TryGetSingleProperty("status", out status);
            value.TryGetSingleProperty("longestFrameUs", out longest);
            value.TryGetSingleProperty("p95FrameUs", out p95);
            if (browser.Kind != CanonicalJsonKind.String || resolution.Kind != CanonicalJsonKind.String || scenario.Kind != CanonicalJsonKind.String || warmup.Kind != CanonicalJsonKind.Number || sample.Kind != CanonicalJsonKind.Number || origin.Kind != CanonicalJsonKind.Number || buckets.Kind != CanonicalJsonKind.Array || foreground.Kind != CanonicalJsonKind.Boolean || pause.Kind != CanonicalJsonKind.Boolean || status.Kind != CanonicalJsonKind.String || longest.Kind != CanonicalJsonKind.Number || p95.Kind != CanonicalJsonKind.Number)
            {
                return EvidenceValidationResult.Fail("TYPE", "Performance payload field types are invalid.");
            }
            if (!Contains(new[] { "Chrome", "Edge" }, browser.StringValue) || !Contains(new[] { "1280x720", "1920x1080" }, resolution.StringValue) || !Contains(new[] { "baseline", "stress" }, scenario.StringValue) || (status.StringValue != "PASS" && status.StringValue != "FAIL")) return EvidenceValidationResult.Fail("ENUM", "Performance payload enum is invalid.");
            if (!string.Equals(browser.StringValue, expectedBrowser, StringComparison.Ordinal)) return EvidenceValidationResult.Fail("MISSING_CELL", "Performance browser does not match the matrix cell.");
            if (!string.Equals(resolution.StringValue, expectedResolution, StringComparison.Ordinal)) return EvidenceValidationResult.Fail("MISSING_CELL", "Performance resolution does not match the matrix cell.");
            if (!string.Equals(scenario.StringValue, expectedScenario, StringComparison.Ordinal)) return EvidenceValidationResult.Fail("MISSING_CELL", "Performance scenario does not match the matrix cell.");

            long warmupValue;
            long sampleValue;
            long originValue;
            long longestValue;
            long p95Value;
            if (!TryGetInteger(warmup, out warmupValue) || warmupValue != 10 || !TryGetInteger(sample, out sampleValue) || sampleValue != 60 || !TryGetInteger(origin, out originValue) || originValue < 0 || !TryGetInteger(longest, out longestValue) || longestValue < 0 || !TryGetInteger(p95, out p95Value) || p95Value < 0)
            {
                return EvidenceValidationResult.Fail("TYPE", "Performance numeric values are invalid.");
            }

            var parsedBuckets = new List<PerformanceBucket>();
            foreach (var bucketValue in buckets.Items)
            {
                var bucketShape = ValidateRequiredOnlyObject(bucketValue, new[] { "index", "startUs", "endUs", "completedFrames", "minFpsEquivalent" });
                if (!bucketShape.IsValid) return bucketShape;
                CanonicalJsonValue index;
                CanonicalJsonValue start;
                CanonicalJsonValue end;
                CanonicalJsonValue completed;
                CanonicalJsonValue minFps;
                bucketValue.TryGetSingleProperty("index", out index);
                bucketValue.TryGetSingleProperty("startUs", out start);
                bucketValue.TryGetSingleProperty("endUs", out end);
                bucketValue.TryGetSingleProperty("completedFrames", out completed);
                bucketValue.TryGetSingleProperty("minFpsEquivalent", out minFps);
                long indexValue;
                long startValue;
                long endValue;
                long completedValue;
                if (!TryGetInteger(index, out indexValue) || indexValue < 0 || indexValue >= BucketCount || !TryGetInteger(start, out startValue) || startValue < 0 || !TryGetInteger(end, out endValue) || endValue < 0 || !TryGetInteger(completed, out completedValue) || completedValue < 0 || minFps.Kind != CanonicalJsonKind.Number)
                {
                    return EvidenceValidationResult.Fail("TYPE", "Performance bucket numeric types are invalid.");
                }
                parsedBuckets.Add(new PerformanceBucket((int)indexValue, startValue, endValue, completedValue, minFps.NumberValue));
            }

            var completionTimes = new List<long>(completionTimesUs);
            var durations = new List<long>(frameDurationsUs);
            if (completionTimes.Count == 0 || durations.Count == 0 || completionTimes.Count != durations.Count)
            {
                return EvidenceValidationResult.Fail("FRAME_COUNT", "Performance timestamp and duration records must be nonempty and one-to-one.");
            }
            for (var index = 1; index < completionTimes.Count; index++)
            {
                if (completionTimes[index] < completionTimes[index - 1])
                {
                    return EvidenceValidationResult.Fail("FRAME_COUNT", "Completion timestamps must be monotonic.");
                }
            }

            var bucketValidation = ValidatePerformanceBuckets(originValue, parsedBuckets, completionTimes);
            if (!bucketValidation.IsValid) return bucketValidation;
            var fpsPass = true;
            foreach (var bucket in parsedBuckets)
            {
                if (bucket.CompletedFrames < 45 || bucket.MinFpsEquivalent < 45d)
                {
                    fpsPass = false;
                    break;
                }
            }

            durations.Sort();
            if (durations[0] < 0) return EvidenceValidationResult.Fail("TYPE", "Frame durations must be nonnegative.");
            var expectedLongest = durations[durations.Count - 1];
            var rank = (95 * durations.Count + 99) / 100;
            var expectedP95 = durations[rank - 1];
            if (longestValue != expectedLongest || p95Value != expectedP95) return EvidenceValidationResult.Fail("FRAME_COUNT", "longestFrameUs or p95FrameUs does not match included frame durations.");

            if (!foreground.BooleanValue || !pause.BooleanValue) return EvidenceValidationResult.Fail("FOREGROUND", "Performance sample requires foreground and no-pause evidence.");
            if (!fpsPass) return EvidenceValidationResult.Fail("FPS", "Every bucket must be at least forty-five FPS.");
            return status.StringValue == "PASS"
                ? EvidenceValidationResult.Pass()
                : EvidenceValidationResult.Fail("FPS", "Performance status must PASS when all pass conditions hold.");
        }
        public static EvidenceValidationResult ValidatePerformance(
            CanonicalJsonValue value,
            IEnumerable<FrameCompletionRecord> rawRecords,
            string expectedBrowser,
            string expectedResolution,
            string expectedScenario)
        {
            if (rawRecords == null) return EvidenceValidationResult.Fail("FRAME_COUNT", "Performance raw records are required.");

            var completionTimes = new List<long>();
            var durations = new List<long>();
            var allForeground = true;
            var noPause = true;
            foreach (var record in rawRecords)
            {
                if (record == null) return EvidenceValidationResult.Fail("FRAME_COUNT", "Performance raw records cannot contain null.");
                completionTimes.Add(record.CompletedAtMicroseconds);
                durations.Add(record.DurationMicroseconds);
                allForeground &= record.Foreground;
                noPause &= record.Unpaused;
            }

            var result = ValidatePerformance(value, completionTimes, durations, expectedBrowser, expectedResolution, expectedScenario);
            if (!result.IsValid) return result;
            return allForeground && noPause
                ? EvidenceValidationResult.Pass()
                : EvidenceValidationResult.Fail("FOREGROUND", "Raw performance records include a background or paused frame.");
        }

        private static bool TryGetInteger(CanonicalJsonValue value, out long integer)
        {
            integer = 0;
            if (value == null || value.Kind != CanonicalJsonKind.Number || double.IsNaN(value.NumberValue) || double.IsInfinity(value.NumberValue) || value.NumberValue < long.MinValue || value.NumberValue >= 9223372036854775808d || Math.Floor(value.NumberValue) != value.NumberValue) return false;
            integer = (long)value.NumberValue;
            return true;
        }

        private static bool Contains(IReadOnlyList<string> values, string value)
        {
            foreach (var item in values)
            {
                if (string.Equals(item, value, StringComparison.Ordinal)) return true;
            }
            return false;
        }

        private static bool ContainsAll(HashSet<string> values, IEnumerable<string> required)
        {
            foreach (var item in required)
            {
                if (!values.Contains(item)) return false;
            }
            return true;
        }

        private static bool HasDuplicates(IList<string> values)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var value in values)
            {
                if (!seen.Add(value)) return true;
            }
            return false;
        }

        private static bool ListsEqual(IList<string> left, IList<string> right)
        {
            if (left.Count != right.Count) return false;
            for (var index = 0; index < left.Count; index++)
            {
                if (!string.Equals(left[index], right[index], StringComparison.Ordinal)) return false;
            }
            return true;
        }
    }
}
