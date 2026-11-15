import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useState } from "react";
import { api, unwrap } from "../api/client";
import type { BayDetails } from "../api/types";
import { useAuth } from "../auth/AuthContext";
import { Card, Empty, ErrorMessage, Loading, PageHeader } from "../components/ui";

export function BaysPage() {
  const { hasRole } = useAuth();
  const owner = hasRole("Owner");
  const queryClient = useQueryClient();
  const [name, setName] = useState("");

  const bays = useQuery({ queryKey: ["bays"], queryFn: async () => unwrap(await api.GET("/api/bays")) });
  const refresh = () => queryClient.invalidateQueries({ queryKey: ["bays"] });

  const create = useMutation({
    mutationFn: async () => unwrap(await api.POST("/api/bays", { body: { name } })),
    onSuccess: async () => {
      setName("");
      await refresh();
    },
  });

  return (
    <>
      <PageHeader title="Bays" subtitle="The workshop's capacity: one vehicle per bay at a time." />
      {owner && (
        <form
          className="toolbar"
          onSubmit={(e) => {
            e.preventDefault();
            create.mutate();
          }}
        >
          <input required placeholder="Name of the new bay, e.g. Lift 3" value={name} onChange={(e) => setName(e.target.value)} aria-label="Bay name" />
          <button type="submit" className="primary" disabled={create.isPending}>
            Add bay
          </button>
        </form>
      )}
      <ErrorMessage error={create.error ?? bays.error} />
      {bays.isPending && <Loading />}
      {bays.data && (
        <Card>
          {bays.data.length === 0 ? (
            <Empty>No bays yet.</Empty>
          ) : (
            <table>
              <thead>
                <tr>
                  <th>Bay</th>
                  <th>State</th>
                  {owner && <th />}
                </tr>
              </thead>
              <tbody>
                {bays.data.map((bay) => (
                  <BayRow key={bay.id} bay={bay} owner={owner} onChanged={refresh} />
                ))}
              </tbody>
            </table>
          )}
        </Card>
      )}
    </>
  );
}

function BayRow({ bay, owner, onChanged }: { bay: BayDetails; owner: boolean; onChanged: () => Promise<unknown> }) {
  const [name, setName] = useState(bay.name);
  const path = { params: { path: { id: bay.id } } };
  const rename = useMutation({
    mutationFn: async () => unwrap(await api.PUT("/api/bays/{id}", { ...path, body: { name } })),
    onSuccess: onChanged,
  });
  const toggle = useMutation({
    mutationFn: async () =>
      unwrap(
        bay.isInService
          ? await api.POST("/api/bays/{id}/take-out-of-service", path)
          : await api.POST("/api/bays/{id}/return-to-service", path),
      ),
    onSuccess: onChanged,
  });

  return (
    <tr>
      <td>
        {owner ? (
          <form
            className="inline-form"
            onSubmit={(e) => {
              e.preventDefault();
              rename.mutate();
            }}
          >
            <input value={name} onChange={(e) => setName(e.target.value)} aria-label={`Name of ${bay.name}`} />
            {name !== bay.name && <button type="submit">Rename</button>}
          </form>
        ) : (
          bay.name
        )}
        <ErrorMessage error={rename.error ?? toggle.error} />
      </td>
      <td>{bay.isInService ? "In service" : <span className="warning">Out of service</span>}</td>
      {owner && (
        <td className="number">
          <button type="button" onClick={() => toggle.mutate()} disabled={toggle.isPending}>
            {bay.isInService ? "Take out of service" : "Return to service"}
          </button>
        </td>
      )}
    </tr>
  );
}
