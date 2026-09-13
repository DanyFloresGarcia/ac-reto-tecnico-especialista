import { describe, expect, it } from "vitest";
import { hasErrors, validate } from "../../src/Frontend/src/features/create-event/validation";
import { emptyZone } from "../../src/Frontend/src/features/create-event/types";
import type { EventFormValue } from "../../src/Frontend/src/features/create-event/types";

function validForm(): EventFormValue {
  return {
    name: "Concierto",
    date: "2026-12-01",
    location: "Estadio",
    zones: [{ name: "General", price: "50", capacity: "100" }],
  };
}

describe("validate", () => {
  it("returns no errors for a fully valid form", () => {
    const errors = validate(validForm());
    expect(hasErrors(errors)).toBe(false);
  });

  it("flags an empty name", () => {
    const form = { ...validForm(), name: "" };
    const errors = validate(form);
    expect(errors.name).toBeDefined();
    expect(hasErrors(errors)).toBe(true);
  });

  it("flags zero zones", () => {
    const form = { ...validForm(), zones: [] };
    const errors = validate(form);
    expect(errors.zones).toBeDefined();
  });

  it("flags a zone with capacity 0", () => {
    const form = { ...validForm(), zones: [{ ...emptyZone(), name: "General", price: "10", capacity: "0" }] };
    const errors = validate(form);
    expect(errors.zoneErrors[0].capacity).toBeDefined();
  });

  it("flags a zone with negative price", () => {
    const form = { ...validForm(), zones: [{ ...emptyZone(), name: "General", price: "-1", capacity: "10" }] };
    const errors = validate(form);
    expect(errors.zoneErrors[0].price).toBeDefined();
  });

  it("accepts a zone with price 0", () => {
    const form = { ...validForm(), zones: [{ ...emptyZone(), name: "General", price: "0", capacity: "10" }] };
    const errors = validate(form);
    expect(errors.zoneErrors[0].price).toBeUndefined();
  });
});
