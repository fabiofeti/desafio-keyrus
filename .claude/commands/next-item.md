---
description: 'Mostra o próximo item pendente do plano (P0 antes de P1) e, com confirmação, implementa via add-challenge.'
argument-hint: '[opcional: "go" para implementar sem perguntar]'
---

1. Leia `docs/IMPLEMENTATION-PLAN.md` e liste os itens `- [ ]` de P0 e P1, com o primeiro pendente de P0 destacado (P1 só quando P0 estiver completo; P2 nunca).
2. Para o próximo item, diga em até três linhas: o que é, os skills que vai usar (`scaffold-solution`, `add-module`, `add-entity`, `add-feature`, `add-integration-event`) e como vai provar (gates do `add-challenge`).
3. Se o argumento for `go` ($ARGUMENTS), invoque o skill `add-challenge` com esse item. Caso contrário, pare e espere a confirmação do usuário.
