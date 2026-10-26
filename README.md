# Keyrus Cash Flow — Desafio de Arquitetura

Prova técnica única com duas perspectivas: [Arquiteto de Soluções](docs/solucoes/01-business-and-requirements.md) e [Arquiteto de Software](docs/software/01-design-and-contracts.md).

## Problema
Registrar lançamentos financeiros (créditos/débitos) e consultar saldo diário consolidado. A indisponibilidade do processamento de consolidação não deve interromper o registro. O desafio estabelece pico de **50 requisições/segundo** para consolidação e **máximo de 5% de perda**; comprovar por teste, não por suposição.

## Arquitetura proposta
Monólito modular .NET 10: API e worker compartilhando os módulos de domínio, PostgreSQL com schemas `transactions` e `consolidation`, RabbitMQ e Outbox/Inbox. Consistência eventual; ausência de escrita cruzada.

## Execução local (após implementação)
```bash
docker compose up --build -d
docker compose ps
docker compose logs -f api worker
```
Documentar no README final URLs/portas efetivas, Swagger/OpenAPI, comandos de testes e exemplos `curl` correspondentes à implementação. **Este pacote contém especificação e roteiro; ainda não contém código nem Compose executável.**

## Documentação
- [Plano de implementação](docs/IMPLEMENTATION-PLAN.md)
- [Soluções: negócio e requisitos](docs/solucoes/01-business-and-requirements.md)
- [Soluções: arquitetura e trade-offs](docs/solucoes/02-target-architecture.md)
- [Soluções: riscos, custos e evolução](docs/solucoes/03-operations-and-roadmap.md)
- [Software: design, dados e contratos](docs/software/01-design-and-contracts.md)
- [Software: resiliência, segurança e NFRs](docs/software/02-quality-attributes.md)
- [Software: testes e critérios de aceite](docs/software/03-testing.md)
- [C4 e sequências em Mermaid](docs/diagrams/architecture.md)
- [ADR: arquitetura e isolamento](docs/adr/ADR-001.md)

## Escopo e transparência
Os documentos descrevem decisões de projeto propostas, não uma implementação já verificada. Atualizar evidências conforme o desenvolvimento. Não há migração legada real no enunciado; arquitetura de transição é opcional e não será inventada.
