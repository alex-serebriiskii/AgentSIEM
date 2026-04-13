import { Route, Router } from "@solidjs/router";
import { Suspense } from "solid-js";
import AppLayout from "./layouts/AppLayout";
import Placeholder from "./pages/Placeholder";
import NotFound from "./pages/NotFound";

export default function AppRouter() {
  return (
    <Router root={AppLayout}>
      <Suspense>
        <Route path="/" component={Placeholder} />
        <Route path="/alerts" component={Placeholder} />
        <Route path="/alerts/:id" component={Placeholder} />
        <Route path="/investigate" component={Placeholder} />
        <Route path="/investigate/sessions/:id" component={Placeholder} />
        <Route path="/investigate/agents/:id" component={Placeholder} />
        <Route path="/rules" component={Placeholder} />
        <Route path="/rules/new" component={Placeholder} />
        <Route path="/rules/:id/edit" component={Placeholder} />
        <Route path="/admin" component={Placeholder} />
        <Route path="/admin/lists/:id" component={Placeholder} />
        <Route path="*404" component={NotFound} />
      </Suspense>
    </Router>
  );
}
