import { ADMIN_DEMO_TOKEN, API_EVENT_URL } from "../../config";
import type { EventFormValue } from "./types";

export interface ZoneDto {
  id: string;
  name: string;
  price: number;
  capacity: number;
}

export interface EventDto {
  id: string;
  name: string;
  date: string;
  location: string;
  status: string;
  zones: ZoneDto[];
}

export class ApiError extends Error {
  status: number;
  fieldErrors?: Record<string, string[]>;

  constructor(message: string, status: number, fieldErrors?: Record<string, string[]>) {
    super(message);
    this.status = status;
    this.fieldErrors = fieldErrors;
  }
}

export async function createEvent(form: EventFormValue): Promise<EventDto> {
  const response = await fetch(`${API_EVENT_URL}/events`, {
    method: "POST",
    headers: {
      "Content-Type": "application/json",
      Authorization: `Bearer ${ADMIN_DEMO_TOKEN}`,
      "X-Correlation-Id": crypto.randomUUID(),
    },
    body: JSON.stringify({
      name: form.name,
      date: new Date(form.date).toISOString(),
      location: form.location,
      zones: form.zones.map((z) => ({
        name: z.name,
        price: Number(z.price),
        capacity: Number(z.capacity),
      })),
    }),
  });

  if (response.status === 201) {
    return (await response.json()) as EventDto;
  }

  if (response.status === 400) {
    const problem = await response.json().catch(() => null);
    throw new ApiError("Revisá los campos marcados.", response.status, problem?.errors);
  }

  if (response.status === 401 || response.status === 403) {
    throw new ApiError("No tenés permiso para crear eventos con este token.", response.status);
  }

  throw new ApiError("Ocurrió un error inesperado. Intentá nuevamente.", response.status);
}
