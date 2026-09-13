import type { EventFormValue, FormErrors, ZoneFieldErrors } from "./types";

// Mirrors the backend's validation rules (spec FR-002 / contracts/events-api.md) so the user
// sees the same errors before the request ever reaches the server.
export function validate(form: EventFormValue): FormErrors {
  const errors: FormErrors = { zoneErrors: form.zones.map(() => ({})) };

  if (!form.name.trim()) {
    errors.name = "El nombre es requerido.";
  } else if (form.name.length > 200) {
    errors.name = "El nombre no puede exceder 200 caracteres.";
  }

  if (!form.date) {
    errors.date = "La fecha es requerida.";
  }

  if (!form.location.trim()) {
    errors.location = "El lugar es requerido.";
  } else if (form.location.length > 300) {
    errors.location = "El lugar no puede exceder 300 caracteres.";
  }

  if (form.zones.length === 0) {
    errors.zones = "Se necesita al menos una zona.";
  }

  form.zones.forEach((zone, index) => {
    const zoneErrors: ZoneFieldErrors = {};

    if (!zone.name.trim()) {
      zoneErrors.name = "El nombre de la zona es requerido.";
    }

    const capacity = Number(zone.capacity);
    if (zone.capacity.trim() === "" || Number.isNaN(capacity) || capacity <= 0) {
      zoneErrors.capacity = "La capacidad debe ser mayor que 0.";
    }

    const price = Number(zone.price);
    if (zone.price.trim() === "" || Number.isNaN(price) || price < 0) {
      zoneErrors.price = "El precio no puede ser negativo.";
    }

    errors.zoneErrors[index] = zoneErrors;
  });

  return errors;
}

export function hasErrors(errors: FormErrors): boolean {
  return Boolean(
    errors.name ||
      errors.date ||
      errors.location ||
      errors.zones ||
      errors.zoneErrors.some((z) => z.name || z.price || z.capacity),
  );
}
