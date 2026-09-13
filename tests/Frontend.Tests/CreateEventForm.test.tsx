import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { CreateEventForm } from "../../src/Frontend/src/features/create-event/CreateEventForm";

function fillRequiredFields() {
  const nameFields = screen.getAllByLabelText(/^nombre$/i);
  fireEvent.change(nameFields[0], { target: { value: "Concierto" } }); // event name
  fireEvent.change(screen.getByLabelText("Fecha"), { target: { value: "2026-12-01" } });
  fireEvent.change(screen.getByLabelText("Lugar"), { target: { value: "Estadio" } });
  fireEvent.change(nameFields[1], { target: { value: "General" } }); // zone name
}

describe("CreateEventForm", () => {
  beforeEach(() => {
    vi.stubGlobal("crypto", { randomUUID: () => "00000000-0000-0000-0000-000000000000" });
  });

  afterEach(() => {
    vi.unstubAllGlobals();
    vi.restoreAllMocks();
  });

  it("shows inline validation errors and does not call fetch when required fields are empty", async () => {
    const fetchSpy = vi.spyOn(global, "fetch");
    render(<CreateEventForm />);

    fireEvent.click(screen.getByRole("button", { name: /guardar/i }));

    expect(await screen.findByText("El nombre es requerido.")).toBeInTheDocument();
    expect(fetchSpy).not.toHaveBeenCalled();
  });

  it("shows a loading state and then a success message on a valid submit", async () => {
    let resolveFetch: (value: unknown) => void = () => {};
    vi.spyOn(global, "fetch").mockReturnValue(
      new Promise((resolve) => {
        resolveFetch = resolve;
      }) as Promise<Response>,
    );

    render(<CreateEventForm />);
    fillRequiredFields();
    fireEvent.change(screen.getAllByLabelText(/precio/i)[0], { target: { value: "50" } });
    fireEvent.change(screen.getAllByLabelText(/capacidad/i)[0], { target: { value: "100" } });

    fireEvent.click(screen.getByRole("button", { name: /guardar/i }));

    // fetch hasn't resolved yet: the component must already be showing the loading state.
    expect(screen.getByRole("button", { name: /guardando/i })).toBeDisabled();

    resolveFetch({
      status: 201,
      json: async () => ({ id: "abc-123", name: "Concierto", zones: [] }),
    });

    expect(await screen.findByRole("status")).toHaveTextContent(/creado con id abc-123/);
  });

  it("shows an error banner when the server rejects the request", async () => {
    vi.spyOn(global, "fetch").mockResolvedValue({
      status: 403,
      json: async () => ({}),
    } as Response);

    render(<CreateEventForm />);
    fillRequiredFields();
    fireEvent.change(screen.getAllByLabelText(/precio/i)[0], { target: { value: "50" } });
    fireEvent.change(screen.getAllByLabelText(/capacidad/i)[0], { target: { value: "100" } });

    fireEvent.click(screen.getByRole("button", { name: /guardar/i }));

    await waitFor(() => {
      expect(screen.getByRole("alert")).toHaveTextContent(/no tenés permiso/i);
    });
  });
});
