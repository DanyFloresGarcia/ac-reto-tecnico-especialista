export interface ZoneFormValue {
  name: string;
  price: string;
  capacity: string;
}

export interface EventFormValue {
  name: string;
  date: string;
  location: string;
  zones: ZoneFormValue[];
}

export interface ZoneFieldErrors {
  name?: string;
  price?: string;
  capacity?: string;
}

export interface FormErrors {
  name?: string;
  date?: string;
  location?: string;
  zones?: string;
  zoneErrors: ZoneFieldErrors[];
}

export function emptyZone(): ZoneFormValue {
  return { name: "", price: "", capacity: "" };
}

export function emptyForm(): EventFormValue {
  return { name: "", date: "", location: "", zones: [emptyZone()] };
}
