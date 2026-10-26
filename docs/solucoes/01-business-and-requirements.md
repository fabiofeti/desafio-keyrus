# Soluções — capacidades, domínios e requisitos

## Contexto de negócio
Um comerciante registra créditos e débitos e consulta relatório de saldo diário consolidado. O enunciado não exige conciliação bancária, pagamentos, contas múltiplas, estornos nem integração com bancos.

## Capacidades e ownership
| Capacidade | Bounded context | Responsabilidade |
|---|---|---|
| Registrar e consultar lançamentos | Transactions | Validação, idempotência, histórico e eventos |
| Consolidar e consultar saldo por dia | Consolidation | Agregação, deduplicação e leitura otimizada |

`Shared.Contracts` não é domínio: contém apenas contratos de integração versionados e tipos técnicos mínimos.

## Requisitos funcionais refinados (propostas)
- RF01 Criar lançamento de crédito ou débito, com valor positivo, data e descrição opcional.
- RF02 Consultar lançamentos por data.
- RF03 Obter total de créditos, total de débitos e saldo líquido de um dia.
- RF04 Atualizar consolidação de forma assíncrona após novos lançamentos.
- RF05 Evitar lançamento duplicado quando cliente repete mesma idempotency key.

## Não funcionais
- RNF01 Queda do Worker de Consolidation não impede gravar Transactions.
- RNF02 Suportar teste de pico de 50 RPS nas consultas de consolidado com perda de requisições <= 5%, conforme definição operacional no documento de testes.
- RNF03 Reprocessamento de eventos sem duplicar saldos.
- RNF04 Segregação de gravações por schema/role e contratos entre domínios.
- RNF05 Segurança, logs, rastreabilidade e proteção de segredos proporcionais ao MVP.

## Premissas a validar
- Moeda única (BRL), precisão `numeric(18,2)` e datas de negócio no fuso America/Sao_Paulo.
- Um comerciante lógico no MVP; multi-tenant fica fora do escopo.
- Saldo diário = soma(créditos) - soma(débitos), **não saldo acumulado**; documentar caso o avaliador interprete diferente.
- Consistência eventual aceita; relatório pode ficar temporariamente defasado durante falhas.

## Fora do escopo
Conciliação bancária, integração ERP, autenticação completa, relatórios mensais, UI e migração legada.
