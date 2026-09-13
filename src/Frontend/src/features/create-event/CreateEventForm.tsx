import { useState } from "react";
import { ApiError, createEvent, type EventDto } from "./api";
import { emptyForm, emptyZone } from "./types";
import type { EventFormValue } from "./types";
import { hasErrors, validate } from "./validation";

export function CreateEventForm() {
  const [form, setForm] = useState<EventFormValue>(emptyForm());
  const [errors, setErrors] = useState(validate(emptyForm()));
  const [touched, setTouched] = useState(false);
  const [loading, setLoading] = useState(false);
  const [errorBanner, setErrorBanner] = useState<string | null>(null);
  const [created, setCreated] = useState<EventDto | null>(null);

  const canRemoveZone = form.zones.length > 1;

  function updateField<K extends keyof EventFormValue>(field: K, value: EventFormValue[K]) {
    const next = { ...form, [field]: value };
    setForm(next);
    setErrors(validate(next));
  }

  function updateZone(index: number, field: "name" | "price" | "capacity", value: string) {
    const zones = form.zones.map((zone, i) => (i === index ? { ...zone, [field]: value } : zone));
    const next = { ...form, zones };
    setForm(next);
    setErrors(validate(next));
  }

  function addZone() {
    const next = { ...form, zones: [...form.zones, emptyZone()] };
    setForm(next);
    setErrors(validate(next));
  }

  function removeZone(index: number) {
    const zones = form.zones.filter((_, i) => i !== index);
    const next = { ...form, zones };
    setForm(next);
    setErrors(validate(next));
  }

  async function handleSubmit(event: React.FormEvent) {
    event.preventDefault();
    setTouched(true);
    setErrorBanner(null);
    setCreated(null);

    const currentErrors = validate(form);
    setErrors(currentErrors);
    if (hasErrors(currentErrors)) {
      return;
    }

    setLoading(true);
    try {
      const result = await createEvent(form);
      setCreated(result);
      setForm(emptyForm());
      setErrors(validate(emptyForm()));
      setTouched(false);
    } catch (error) {
      if (error instanceof ApiError) {
        setErrorBanner(error.message);
      } else {
        setErrorBanner("No se pudo conectar con el servidor. Intentá nuevamente.");
      }
    } finally {
      setLoading(false);
    }
  }

  const saveDisabled = loading || form.zones.length === 0;

  return (
    <form onSubmit={handleSubmit} className="mx-auto max-w-2xl space-y-6 p-6">
      <h1 className="text-2xl font-semibold">Registrar Evento</h1>

      {errorBanner && (
        <div role="alert" className="rounded border border-red-400 bg-red-50 p-3 text-red-700">
          {errorBanner}
        </div>
      )}

      {created && (
        <div role="status" className="rounded border border-green-400 bg-green-50 p-3 text-green-700">
          Evento "{created.name}" creado con id {created.id}.
        </div>
      )}

      <div>
        <label htmlFor="name" className="block font-medium">
          Nombre
        </label>
        <input
          id="name"
          type="text"
          value={form.name}
          onChange={(e) => updateField("name", e.target.value)}
          className="mt-1 w-full rounded border border-gray-300 p-2"
        />
        {touched && errors.name && <p className="mt-1 text-sm text-red-600">{errors.name}</p>}
      </div>

      <div>
        <label htmlFor="date" className="block font-medium">
          Fecha
        </label>
        <input
          id="date"
          type="date"
          value={form.date}
          onChange={(e) => updateField("date", e.target.value)}
          className="mt-1 w-full rounded border border-gray-300 p-2"
        />
        {touched && errors.date && <p className="mt-1 text-sm text-red-600">{errors.date}</p>}
      </div>

      <div>
        <label htmlFor="location" className="block font-medium">
          Lugar
        </label>
        <input
          id="location"
          type="text"
          value={form.location}
          onChange={(e) => updateField("location", e.target.value)}
          className="mt-1 w-full rounded border border-gray-300 p-2"
        />
        {touched && errors.location && <p className="mt-1 text-sm text-red-600">{errors.location}</p>}
      </div>

      <fieldset className="space-y-3">
        <legend className="font-medium">Zonas</legend>
        {touched && errors.zones && <p className="text-sm text-red-600">{errors.zones}</p>}

        {form.zones.map((zone, index) => {
          const zoneErrors = errors.zoneErrors[index] ?? {};
          const nameId = `zone-${index}-name`;
          const priceId = `zone-${index}-price`;
          const capacityId = `zone-${index}-capacity`;
          return (
            <div key={index} className="grid grid-cols-[2fr_1fr_1fr_auto] items-start gap-2 rounded border border-gray-200 p-3">
              <div>
                <label htmlFor={nameId} className="block text-sm">Nombre</label>
                <input
                  id={nameId}
                  type="text"
                  value={zone.name}
                  onChange={(e) => updateZone(index, "name", e.target.value)}
                  className="w-full rounded border border-gray-300 p-1"
                />
                {touched && zoneErrors.name && <p className="text-xs text-red-600">{zoneErrors.name}</p>}
              </div>
              <div>
                <label htmlFor={priceId} className="block text-sm">Precio</label>
                <input
                  id={priceId}
                  type="number"
                  step="0.01"
                  value={zone.price}
                  onChange={(e) => updateZone(index, "price", e.target.value)}
                  className="w-full rounded border border-gray-300 p-1"
                />
                {touched && zoneErrors.price && <p className="text-xs text-red-600">{zoneErrors.price}</p>}
              </div>
              <div>
                <label htmlFor={capacityId} className="block text-sm">Capacidad</label>
                <input
                  id={capacityId}
                  type="number"
                  value={zone.capacity}
                  onChange={(e) => updateZone(index, "capacity", e.target.value)}
                  className="w-full rounded border border-gray-300 p-1"
                />
                {touched && zoneErrors.capacity && <p className="text-xs text-red-600">{zoneErrors.capacity}</p>}
              </div>
              <button
                type="button"
                onClick={() => removeZone(index)}
                disabled={!canRemoveZone}
                className="mt-6 rounded border border-gray-300 px-2 py-1 text-sm disabled:opacity-40"
              >
                Eliminar zona
              </button>
            </div>
          );
        })}

        <button type="button" onClick={addZone} className="rounded border border-gray-400 px-3 py-1 text-sm">
          + Agregar zona
        </button>
      </fieldset>

      <button
        type="submit"
        disabled={saveDisabled}
        className="rounded bg-blue-600 px-4 py-2 text-white disabled:opacity-50"
      >
        {loading ? "Guardando…" : "Guardar"}
      </button>
    </form>
  );
}
